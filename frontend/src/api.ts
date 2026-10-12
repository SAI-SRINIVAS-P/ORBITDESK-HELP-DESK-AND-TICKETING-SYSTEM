export type Role = 'User' | 'Agent' | 'Admin'
export type LoginAs = 'User' | 'Admin'
export type TicketStatus = 'New' | 'Open' | 'InProgress' | 'Resolved' | 'Closed'
export type TicketPriority = 'Low' | 'Medium' | 'High' | 'Urgent'

export interface Session {
  token: string
  expiresAtUtc: string
  userId: string
  displayName: string
  email: string
  roles: Role[]
}
export interface Ticket {
  id: string
  number: string
  title: string
  category: string
  priority: TicketPriority
  status: TicketStatus
  requesterId: string
  requesterName: string
  assigneeId: string | null
  assigneeName: string | null
  createdAtUtc: string
  updatedAtUtc: string
}
export interface Comment {
  id: string
  authorId: string
  authorName: string
  body: string
  isInternal: boolean
  createdAtUtc: string
}
export interface HistoryEntry {
  id: string
  actorName: string
  field: string
  oldValue: string | null
  newValue: string | null
  changedAtUtc: string
}
export interface TicketDetail extends Ticket {
  description: string
  comments: Comment[]
  history: HistoryEntry[]
}
export interface Summary {
  total: number
  new: number
  inProgress: number
  resolved: number
  urgent: number
  assignedToMe: number
}
export interface Agent {
  id: string
  displayName: string
  email: string
}
export interface UserRecord {
  id: string
  displayName: string
  email: string
  roles: Role[]
  createdAtUtc: string
}

const SESSION_KEY = 'orbitdesk-session'
const API_ROOT = (import.meta.env.VITE_API_URL as string | undefined) ?? '/api'

export function getSession(): Session | null {
  const raw = localStorage.getItem(SESSION_KEY)
  if (!raw) return null
  try {
    const value = JSON.parse(raw) as Session
    if (new Date(value.expiresAtUtc).getTime() <= Date.now()) {
      localStorage.removeItem(SESSION_KEY)
      return null
    }
    return value
  } catch {
    localStorage.removeItem(SESSION_KEY)
    return null
  }
}

export function saveSession(session: Session): void {
  localStorage.setItem(SESSION_KEY, JSON.stringify(session))
}
export function clearSession(): void {
  localStorage.removeItem(SESSION_KEY)
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const session = getSession()
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  if (session?.token) headers.set('Authorization', `Bearer ${session.token}`)
  let response: Response
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), 20000)
  try {
    response = await fetch(`${API_ROOT}${path}`, { ...init, headers, signal: controller.signal })
  } catch {
    if (controller.signal.aborted) throw new Error('The request timed out after 20 seconds. Check the API logs and try again.')
    throw new Error('OrbitDesk API is unreachable. Start the API and try again.')
  } finally {
    window.clearTimeout(timeoutId)
  }
  if (!response.ok) {
    let message = `Request failed (${response.status})`
    try {
      const payload = await response.json() as { message?: string; title?: string; errors?: { message?: string }[] }
      message = payload.message ?? payload.title ?? payload.errors?.map(e => e.message).filter(Boolean).join(' ') ?? message
    } catch {
      // Keep the readable fallback for non-JSON errors.
    }
    if (response.status === 401 && session) {
      clearSession()
      window.dispatchEvent(new Event('orbitdesk:unauthorized'))
    }
    throw new Error(message)
  }
  if (response.status === 204) return undefined as T
  return await response.json() as T
}

export const api = {
  login: (email: string, password: string, loginAs: LoginAs) => request<Session>('/auth/login', { method: 'POST', body: JSON.stringify({ email, password, loginAs }) }),
  register: (displayName: string, email: string, password: string) => request<Session>('/auth/register', { method: 'POST', body: JSON.stringify({ displayName, email, password }) }),
  deleteAccount: (password: string, confirmation: string) => request<void>('/auth/me', { method: 'DELETE', body: JSON.stringify({ password, confirmation }) }),
  tickets: (filters?: { q?: string; status?: TicketStatus | ''; priority?: TicketPriority | '' }) => {
    const query = new URLSearchParams()
    if (filters?.q) query.set('q', filters.q)
    if (filters?.status) query.set('status', filters.status)
    if (filters?.priority) query.set('priority', filters.priority)
    const suffix = query.size ? `?${query.toString()}` : ''
    return request<Ticket[]>(`/tickets${suffix}`)
  },
  summary: () => request<Summary>('/tickets/summary'),
  ticket: (id: string) => request<TicketDetail>(`/tickets/${id}`),
  createTicket: (values: { title: string; description: string; category: string; priority: TicketPriority }) => request<Ticket>('/tickets', { method: 'POST', body: JSON.stringify(values) }),
  addComment: (id: string, body: string, isInternal: boolean) => request<Comment>(`/tickets/${id}/comments`, { method: 'POST', body: JSON.stringify({ body, isInternal }) }),
  updateStatus: (id: string, status: TicketStatus, note?: string) => request<{ id: string; status: TicketStatus; updatedAtUtc: string }>(`/tickets/${id}/status`, { method: 'PATCH', body: JSON.stringify({ status, note }) }),
  respondToClosure: (id: string, confirm: boolean, note?: string) => request<{ id: string; status: TicketStatus; updatedAtUtc: string; closureConfirmed: boolean }>(`/tickets/${id}/closure-response`, { method: 'POST', body: JSON.stringify({ confirm, note }) }),
  assign: (id: string, agentId: string) => request(`/tickets/${id}/assignee`, { method: 'PUT', body: JSON.stringify({ agentId }) }),
  agents: () => request<Agent[]>('/agents'),
  users: () => request<UserRecord[]>('/admin/users'),
  updateRole: (id: string, role: Role) => request(`/admin/users/${id}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
}
