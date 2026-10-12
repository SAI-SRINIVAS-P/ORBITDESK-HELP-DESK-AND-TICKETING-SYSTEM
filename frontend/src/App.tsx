import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Activity, ArrowDownRight, ArrowRight, ArrowUpRight, Bell, Check, CheckCheck, ChevronDown,
  CircleHelp, Clock3, FileClock, Filter, Headphones, Inbox, LayoutDashboard,
  ListFilter, LogOut, MessageCircle, Plus, Search, Send, ShieldCheck,
  Sparkles, Ticket as TicketIcon, Users, X, Zap, Trash2, AlertTriangle,
} from 'lucide-react'
import { api, clearSession, getSession, saveSession, type Agent, type Role, type Session, type Ticket, type TicketDetail, type TicketPriority, type TicketStatus, type UserRecord, type LoginAs } from './api'

const statuses: TicketStatus[] = ['New', 'Open', 'InProgress', 'Resolved', 'Closed']
const allowedNextStatuses = (current: TicketStatus, isAdmin: boolean): TicketStatus[] => {
  switch (current) {
    case 'New': return ['Open']
    case 'Open': return ['InProgress', 'Resolved']
    case 'InProgress': return ['Open', 'Resolved']
    case 'Resolved': return ['InProgress'] // Closure needs requester confirmation.
    case 'Closed': return isAdmin ? ['Open'] : []
  }
  return []
}
const priorities: TicketPriority[] = ['Low', 'Medium', 'High', 'Urgent']
const categories = ['Account access', 'Billing & payments', 'Technical issue', 'Feature request', 'Bug report', 'Other']
const initials = (name?: string) => (name || 'Orbit User').split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase()
const niceStatus = (status: TicketStatus) => status === 'InProgress' ? 'In progress' : status
const dateShort = (value: string) => new Intl.DateTimeFormat('en', { month: 'short', day: 'numeric' }).format(new Date(value))
const dateLong = (value: string) => new Intl.DateTimeFormat('en', { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' }).format(new Date(value))
const relative = (value: string) => {
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60000))
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  if (minutes < 1440) return `${Math.floor(minutes / 60)}h ago`
  if (minutes < 10080) return `${Math.floor(minutes / 1440)}d ago`
  return dateShort(value)
}

export default function App() {
  const [session, setSession] = useState<Session | null>(() => getSession())
  const [authMode, setAuthMode] = useState<'login' | 'register'>('login')
  const [view, setView] = useState<'overview' | 'tickets' | 'team'>('overview')
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<TicketStatus | ''>('')
  const [priorityFilter, setPriorityFilter] = useState<TicketPriority | ''>('')
  const [createOpen, setCreateOpen] = useState(false)
  const [selectedTicketId, setSelectedTicketId] = useState<string | null>(null)
  const [toast, setToast] = useState('')
  const [deleteAccountOpen, setDeleteAccountOpen] = useState(false)
  const [authNotice, setAuthNotice] = useState('')
  const queryClient = useQueryClient()
  const isAgent = Boolean(session?.roles.some(role => role === 'Agent' || role === 'Admin'))
  const isAdmin = Boolean(session?.roles.includes('Admin'))
  const canDeleteAccount = Boolean(session?.roles.includes('User') && !session?.roles.includes('Agent') && !session?.roles.includes('Admin'))

  useEffect(() => {
    const sync = () => setSession(getSession())
    window.addEventListener('storage', sync)
    window.addEventListener('orbitdesk:unauthorized', sync)
    return () => {
      window.removeEventListener('storage', sync)
      window.removeEventListener('orbitdesk:unauthorized', sync)
    }
  }, [])
  useEffect(() => {
    if (!toast) return
    const timeout = window.setTimeout(() => setToast(''), 3200)
    return () => window.clearTimeout(timeout)
  }, [toast])

  const filters = useMemo(() => ({ q: query, status: statusFilter, priority: priorityFilter }), [query, statusFilter, priorityFilter])
  const ticketsQuery = useQuery({ queryKey: ['tickets', filters], queryFn: () => api.tickets(filters), enabled: Boolean(session) })
  const summaryQuery = useQuery({ queryKey: ['summary'], queryFn: api.summary, enabled: Boolean(session) })
  const agentsQuery = useQuery({ queryKey: ['agents'], queryFn: api.agents, enabled: Boolean(session && isAgent) })
  const usersQuery = useQuery({ queryKey: ['users'], queryFn: api.users, enabled: Boolean(session && isAdmin && view === 'team') })
  const selectedQuery = useQuery({ queryKey: ['ticket', selectedTicketId], queryFn: () => api.ticket(selectedTicketId!), enabled: Boolean(session && selectedTicketId) })

  const invalidateTickets = async (id?: string) => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['tickets'] }),
      queryClient.invalidateQueries({ queryKey: ['summary'] }),
      ...(id ? [queryClient.invalidateQueries({ queryKey: ['ticket', id] })] : []),
    ])
  }
  const signOut = () => {
    clearSession()
    setSession(null)
    setAuthMode('login')
    queryClient.clear()
  }

  if (!session) {
    return <AuthScreen mode={authMode} setMode={setAuthMode} notice={authNotice} onNoticeDismiss={() => setAuthNotice('')} onSuccess={value => { saveSession(value); setSession(value); setToast('Welcome to OrbitDesk'); setAuthNotice('') }} />
  }

  const tickets = ticketsQuery.data ?? []
  const summary = summaryQuery.data
  const openTicket = (ticket: Ticket) => setSelectedTicketId(ticket.id)
  const changeRole = async (userId: string, role: Role) => {
    try {
      await api.updateRole(userId, role)
      await queryClient.invalidateQueries({ queryKey: ['users'] })
      setToast('Role permissions updated')
    } catch (error) { setToast(error instanceof Error ? error.message : 'Could not update role') }
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand-lockup"><div className="brand-symbol"><Headphones size={21} strokeWidth={2.5} /></div><div className="brand-word">orbit<span>desk</span><small>SUPPORT OS</small></div></div>
        <div className="workspace-switch"><div className="workspace-icon">O</div><div className="workspace-copy"><strong>Orbit Labs</strong><span>Workspace</span></div><ChevronDown size={15} /></div>
        <div className="side-label">WORKSPACE</div>
        <nav className="side-nav" aria-label="Main navigation">
          <button className={`nav-item ${view === 'overview' ? 'active' : ''}`} onClick={() => setView('overview')}><LayoutDashboard size={17} />Overview</button>
          <button className={`nav-item ${view === 'tickets' ? 'active' : ''}`} onClick={() => setView('tickets')}><Inbox size={17} />All tickets<span className="nav-count">{summary?.total ?? '–'}</span></button>
          <button className="nav-item" onClick={() => { setStatusFilter('New'); setView('tickets') }}><Zap size={17} />Needs attention{summary?.new ? <span className="nav-dot" /> : null}</button>
          {isAdmin && <button className={`nav-item ${view === 'team' ? 'active' : ''}`} onClick={() => setView('team')}><Users size={17} />Team & access</button>}
        </nav>
        <div className="side-label secondary-label">YOUR SPACE</div>
        <nav className="side-nav">
          <button className="nav-item" onClick={() => { setStatusFilter('InProgress'); setView('tickets') }}><Activity size={17} />In progress</button>
          <button className="nav-item" onClick={() => { setStatusFilter('Resolved'); setView('tickets') }}><CheckCheck size={17} />Resolved</button>
        </nav>
        <div className="sidebar-spacer" />
        <div className="sidebar-help"><div className="help-orbit"><CircleHelp size={18} /></div><strong>Need a hand?</strong><p>Our help center has answers for the curious.</p><button onClick={() => setToast('Help center is coming soon.')}>Visit help center <ArrowRight size={13} /></button></div>
        <>{canDeleteAccount && <button className="account-delete-link" onClick={() => setDeleteAccountOpen(true)}><Trash2 size={14}/> Delete my account</button>}<div className="sidebar-bottom"><div className="avatar avatar-purple">{initials(session.displayName)}</div><div className="profile-copy"><strong>{session.displayName}</strong><span>{session.roles.join(' · ')}</span></div><button className="icon-button logout" title="Sign out" onClick={signOut}><LogOut size={16} /></button></div></>
      </aside>

      <main className="main-area">
        <header className="topbar"><div className="breadcrumbs"><span>Workspace</span><span className="crumb-slash">/</span><strong>{view === 'overview' ? 'Overview' : view === 'tickets' ? 'Tickets' : 'Team & access'}</strong></div><div className="top-actions"><div className="secure-pill"><span />All systems operational</div><button className="icon-button notification-button" title="Notifications" onClick={() => setToast('You’re all caught up.')}><Bell size={17} /><i /></button><div className="top-divider"/><div className="top-avatar">{initials(session.displayName)}</div></div></header>
        <div className="content-wrap">
          {view === 'team' && isAdmin ? (
            <TeamView users={usersQuery.data ?? []} loading={usersQuery.isLoading} onRoleChange={changeRole} />
          ) : view === 'tickets' ? (
            <>
              <div className="page-heading row-heading"><div><div className="eyebrow"><span className="eyebrow-line" />SUPPORT INBOX</div><h1>All tickets<span className="heading-period">.</span></h1><p className="page-subtitle">The full picture, one conversation at a time.</p></div>{!isAgent && <button className="primary-button" onClick={() => setCreateOpen(true)}><Plus size={17} />New ticket</button>}</div>
              <TicketControls query={query} setQuery={setQuery} status={statusFilter} setStatus={setStatusFilter} priority={priorityFilter} setPriority={setPriorityFilter} />
              <TicketTable tickets={tickets} loading={ticketsQuery.isLoading} onOpen={openTicket} />
            </>
          ) : (
            <>
              <div className="page-heading row-heading"><div><div className="eyebrow"><span className="eyebrow-line" />{new Intl.DateTimeFormat('en', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }).format(new Date()).toUpperCase()}</div><h1>{greeting()}, {session.displayName.split(' ')[0]}<span className="heading-period">.</span></h1><p className="page-subtitle">Here’s what’s happening in your support space today.</p></div>{!isAgent && <button className="primary-button" onClick={() => setCreateOpen(true)}><Plus size={17} />Create a ticket</button>}</div>
              <section className="hero-banner"><div className="hero-copy"><div className="hero-chip"><Sparkles size={13} /> YOUR SUPPORT, IN ORBIT</div><h2>Better support starts<br/>with <span>one clear view.</span></h2><p>Every conversation, every handoff, every win. All right here.</p><button className="hero-button" onClick={() => setView('tickets')}>Explore your inbox <ArrowRight size={15} /></button></div><div className="hero-art" aria-hidden="true"><div className="orbit orbit-one"/><div className="orbit orbit-two"/><div className="planet planet-large"/><div className="planet planet-small"/><div className="star star-one">✦</div><div className="star star-two">✧</div><div className="art-card"><span className="art-card-dot"/><div><b>Ticket resolved</b><small>Just now · Nice work!</small></div><Check size={15}/></div><div className="art-glow"/></div><div className="hero-noise"/></section>
              <div className="section-heading"><div><h2>Your pulse</h2><p>A little snapshot of the queue.</p></div><span className="updated-label"><span/> LIVE OVERVIEW</span></div>
              {summaryQuery.isError ? <InlineError message={summaryQuery.error.message} /> : <div className="stats-grid">
                <StatCard icon={<TicketIcon size={18}/>} label="Total tickets" value={summary?.total} note="Across your workspace" tint="violet" />
                <StatCard icon={<Clock3 size={18}/>} label="New tickets" value={summary?.new} note="Waiting for a first look" tint="blue" trend="up" />
                <StatCard icon={<Activity size={18}/>} label="In progress" value={summary?.inProgress} note={isAgent ? `${summary?.assignedToMe ?? 0} assigned to you` : 'Being worked on'} tint="orange" />
                <StatCard icon={<CheckCheck size={18}/>} label="Resolved" value={summary?.resolved} note="Closed the loop" tint="green" trend="down" />
              </div>}
              <div className="dashboard-columns"><section className="panel recent-panel"><div className="panel-heading"><div><h2>{isAgent ? 'Latest in the queue' : 'Your latest tickets'}</h2><p>{isAgent ? 'Recent activity across the workspace.' : 'Pick up where you left off.'}</p></div><button className="text-button" onClick={() => setView('tickets')}>View all <ArrowRight size={14}/></button></div><TicketTable compact tickets={tickets.slice(0, 5)} loading={ticketsQuery.isLoading} onOpen={openTicket}/></section>
                <aside className="panel queue-panel"><div className="panel-heading"><div><h2>Queue snapshot</h2><p>At a glance, without the noise.</p></div><div className="queue-icon"><ListFilter size={17}/></div></div><QueueRow color="coral" label="Needs attention" count={summary?.urgent} caption="Urgent & unresolved" onClick={() => { setPriorityFilter('Urgent'); setStatusFilter(''); setView('tickets') }}/><QueueRow color="purple" label="Fresh arrivals" count={summary?.new} caption="Newly raised tickets" onClick={() => { setStatusFilter('New'); setView('tickets') }}/><QueueRow color="blue" label="In the works" count={summary?.inProgress} caption="Being handled right now" onClick={() => { setStatusFilter('InProgress'); setView('tickets') }}/><div className="queue-footer"><div className="mini-avatars"><span>J</span><span>M</span><span>{initials(session.displayName)}</span></div><span>{isAgent ? 'Your team is on it' : 'Support has your back'}</span><ArrowUpRight size={14}/></div></aside></div>
              <div className="bottom-note"><div className="bottom-note-icon"><ShieldCheck size={16}/></div><span><strong>Your conversations stay yours.</strong> Role-based access keeps private notes and admin controls in the right hands.</span><span className="note-spark">✦</span></div>
            </>
          )}
          <footer className="app-footer"><span>© 2026 OrbitDesk</span><span>Made for better conversations <span className="footer-heart">♥</span></span><span className="footer-version"><span/> SYSTEMS NOMINAL</span></footer>
        </div>
      </main>

      {deleteAccountOpen && <DeleteAccountModal onClose={() => setDeleteAccountOpen(false)} onDeleted={() => { setDeleteAccountOpen(false); clearSession(); setSession(null); setAuthMode('login'); queryClient.clear(); setAuthNotice('Your account has been deleted and sign-in has been disabled. A confirmation email is queued for delivery.'); }} />}
      {createOpen && !isAgent && <CreateTicketModal onClose={() => setCreateOpen(false)} onCreate={async values => { try { const ticket = await api.createTicket(values); await invalidateTickets(); setCreateOpen(false); setToast(`Ticket ${ticket.number} is in orbit.`); setSelectedTicketId(ticket.id) } catch (error) { setToast(error instanceof Error ? error.message : 'Could not create ticket') } }} />}
      {selectedTicketId && <TicketDrawer ticket={selectedQuery.data} loading={selectedQuery.isLoading} error={selectedQuery.error instanceof Error ? selectedQuery.error.message : ''} agents={agentsQuery.data ?? []} session={session} onClose={() => setSelectedTicketId(null)} onRefresh={() => invalidateTickets(selectedTicketId)} onToast={setToast}/>}
      {toast && <div className="toast" role="status"><div className="toast-check"><Check size={15}/></div><span>{toast}</span><button onClick={() => setToast('')} aria-label="Dismiss"><X size={15}/></button></div>}
    </div>
  )
}

function greeting() {
  const hour = new Date().getHours()
  return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening'
}

function AuthScreen({ mode, setMode, onSuccess, notice, onNoticeDismiss }: { mode: 'login' | 'register'; setMode: (mode: 'login' | 'register') => void; onSuccess: (session: Session) => void; notice: string; onNoticeDismiss: () => void }) {
  const [loginAs, setLoginAs] = useState<LoginAs>('User')
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const mutation = useMutation({
    mutationFn: () => mode === 'login'
      ? api.login(email.trim(), password, loginAs)
      : api.register(displayName.trim(), email.trim(), password),
    onSuccess,
    onError: errorValue => setError(errorValue instanceof Error ? errorValue.message : 'Could not sign in')
  })
  const submit = (event: FormEvent) => { event.preventDefault(); setError(''); mutation.mutate() }
  const chooseLoginAs = (choice: LoginAs) => {
    setLoginAs(choice)
    setError('')
    if (choice === 'Admin') setMode('login')
  }

  return <div className="auth-page">
    <div className="auth-left">
      <div className="auth-brand"><div className="brand-symbol"><Headphones size={21}/></div><div className="brand-word">orbit<span>desk</span><small>SUPPORT OS</small></div></div>
      <div className="auth-promo"><div className="hero-chip"><Sparkles size={13}/> LESS CHAOS, MORE CLARITY</div><h1>Good support<br/>has a <span>home.</span></h1><p>Bring every customer conversation into focus. The little details matter, and we keep them all together.</p><div className="auth-benefits"><div><span><Check size={14}/></span>One place for every ticket</div><div><span><Check size={14}/></span>Clear ownership and next steps</div><div><span><Check size={14}/></span>A history you can trust</div></div></div>
      <div className="auth-visual" aria-hidden="true"><div className="auth-visual-ring ring-a"/><div className="auth-visual-ring ring-b"/><div className="auth-visual-core"><Headphones size={32}/></div><div className="auth-floating float-a"><CheckCheck size={17}/><span><b>All caught up</b><small>Resolution feels good</small></span></div><div className="auth-floating float-b"><MessageCircle size={17}/><span><b>New reply</b><small>Your team is on it</small></span></div></div><div className="auth-left-foot"><span>BUILT FOR HUMAN SUPPORT</span><span>01 / 03</span></div>
    </div>
    <section className="auth-right"><div className="auth-form-wrap">
      <div className="auth-mobile-brand"><div className="brand-symbol"><Headphones size={21}/></div><div className="brand-word">orbit<span>desk</span></div></div>
      <div className="auth-kicker">{mode === 'login' ? (loginAs === 'Admin' ? 'ADMINISTRATOR ACCESS' : 'WELCOME BACK') : 'START HERE'}</div>
      <h2>{mode === 'login' ? (loginAs === 'Admin' ? 'Admin sign in.' : 'Good to see you.') : 'Make some space.'}</h2>
      <p className="auth-lede">{mode === 'login' ? (loginAs === 'Admin' ? 'Sign in with your provisioned administrator account.' : 'Sign in and get back to what matters.') : 'Create a user account to keep your support in orbit.'}</p>
      <div className="auth-choice-tabs" aria-label="Choose sign-in type">
        <button type="button" className={loginAs === 'User' ? 'selected' : ''} onClick={() => chooseLoginAs('User')}><Users size={15}/> User</button>
        <button type="button" className={loginAs === 'Admin' ? 'selected' : ''} onClick={() => chooseLoginAs('Admin')}><ShieldCheck size={15}/> Admin</button>
      </div>
      {loginAs === 'User' ? <div className="auth-tabs"><button type="button" className={mode === 'login' ? 'selected' : ''} onClick={() => { setMode('login'); setError('') }}>Sign in</button><button type="button" className={mode === 'register' ? 'selected' : ''} onClick={() => { setMode('register'); setError('') }}>Create account</button></div> : <div className="auth-role-note">Administrator sign-in only. Public sign-up is disabled for admins.</div>}
      {notice && <div className="auth-notice" role="status"><span>{notice}</span><button type="button" aria-label="Dismiss notice" onClick={onNoticeDismiss}><X size={14}/></button></div>}
      <form className="auth-form" onSubmit={submit}>
        {mode === 'register' && loginAs === 'User' && <label>Your name<input autoComplete="name" value={displayName} onChange={e => setDisplayName(e.target.value)} placeholder="How should we call you?" required minLength={2}/></label>}
        <label>Work email<input autoComplete="email" type="email" value={email} onChange={e => setEmail(e.target.value)} placeholder={loginAs === 'Admin' ? 'admin@company.com' : 'you@company.com'} required/></label>
        <label>Password<div className="password-wrap"><input autoComplete={mode === 'login' ? 'current-password' : 'new-password'} type={showPassword ? 'text' : 'password'} value={password} onChange={e => setPassword(e.target.value)} placeholder={mode === 'login' ? 'Enter your password' : 'At least 8 characters'} required minLength={mode === 'register' ? 8 : undefined}/><button type="button" onClick={() => setShowPassword(v => !v)}>{showPassword ? 'Hide' : 'Show'}</button></div></label>
        {error && <div className="form-error">{error}</div>}
        <button className="primary-button auth-submit" disabled={mutation.isPending}>{mutation.isPending ? <span className="spinner"/> : null}{mode === 'login' ? (loginAs === 'Admin' ? 'Sign in as Admin' : 'Sign in as User') : 'Create your account'}<ArrowRight size={16}/></button>
      </form>
      {loginAs === 'User' && <p className="auth-role-footnote">Requester and Agent accounts use User sign-in. Admin accounts use Admin sign-in.</p>}
      <div className="auth-separator"><span/>OR KEEP IT HUMAN<span/></div><div className="auth-security"><ShieldCheck size={17}/><div><strong>Your space is protected</strong><p>Secure sign-in with role-based access.</p></div></div><p className="auth-terms">By continuing, you agree to keep conversations thoughtful and information secure.</p>
    </div><div className="auth-right-foot"><span>© 2026 ORBITDESK</span><span>PRIVACY FIRST <span className="footer-heart">♥</span></span></div></section>
  </div>
}

function DeleteAccountModal({ onClose, onDeleted }: { onClose: () => void; onDeleted: () => void }) {
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setBusy(true)
    try {
      await api.deleteAccount(password, confirmation)
      onDeleted()
    } catch (value) {
      setError(value instanceof Error ? value.message : 'Could not delete account')
    } finally {
      setBusy(false)
    }
  }
  return <div className="modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onClose() }}>
    <section className="create-modal delete-modal" role="dialog" aria-modal="true" aria-labelledby="delete-account-title">
      <div className="modal-header"><div><div className="modal-kicker">ACCOUNT & PRIVACY</div><h2 id="delete-account-title">Delete your account<span>.</span></h2><p>This signs you out and removes access to your profile.</p></div><button className="icon-button modal-close" onClick={onClose} aria-label="Close" disabled={busy}><X size={18}/></button></div>
      <div className="delete-warning"><AlertTriangle size={18}/><p><strong>This cannot be undone.</strong> Your profile will be anonymized and sign-in disabled. Existing tickets and audit history are retained so support records remain consistent. A confirmation email will be sent to your current email address.</p></div>
      <form className="create-form" onSubmit={submit}>
        <label>Current password<input type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} required/></label>
        <label>Type <strong>DELETE</strong> to confirm<input value={confirmation} onChange={event => setConfirmation(event.target.value)} placeholder="DELETE" required autoComplete="off"/></label>
        {error && <div className="form-error">{error}</div>}
        <div className="modal-footer"><button type="button" className="secondary-button" onClick={onClose} disabled={busy}>Keep my account</button><button type="submit" className="danger-button" disabled={busy || confirmation !== 'DELETE' || !password}>{busy ? 'Deleting…' : 'Permanently delete account'}<Trash2 size={15}/></button></div>
      </form>
    </section>
  </div>
}

function StatCard({ icon, label, value, note, tint, trend }: { icon: ReactNode; label: string; value?: number; note: string; tint: string; trend?: 'up' | 'down' }) {
  return <article className="stat-card"><div className={`stat-icon ${tint}`}>{icon}</div><div className="stat-label">{label}</div><div className="stat-value">{value ?? <span className="value-skeleton"/>}</div><div className="stat-foot">{trend && <span className={`trend ${trend}`}>{trend === 'up' ? <ArrowUpRight size={12}/> : <ArrowDownRight size={12}/>}</span>}{note}</div><div className={`stat-accent ${tint}`}/></article>
}

function TicketControls({ query, setQuery, status, setStatus, priority, setPriority }: { query: string; setQuery: (value: string) => void; status: TicketStatus | ''; setStatus: (value: TicketStatus | '') => void; priority: TicketPriority | ''; setPriority: (value: TicketPriority | '') => void }) {
  const searchRef = useRef<HTMLInputElement>(null)
  useEffect(() => {
    const focusSearch = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        searchRef.current?.focus()
      }
    }
    window.addEventListener('keydown', focusSearch)
    return () => window.removeEventListener('keydown', focusSearch)
  }, [])
  return <div className="ticket-controls"><div className="search-control"><Search size={16}/><input ref={searchRef} value={query} onChange={e => setQuery(e.target.value)} placeholder="Search tickets, keywords, people…"/><kbd>⌘ K</kbd></div><div className="select-control"><Filter size={15}/><select value={status} onChange={e => setStatus(e.target.value as TicketStatus | '')}><option value="">All statuses</option>{statuses.map(s => <option key={s} value={s}>{niceStatus(s)}</option>)}</select><ChevronDown size={13}/></div><div className="select-control priority-select"><select value={priority} onChange={e => setPriority(e.target.value as TicketPriority | '')}><option value="">All priorities</option>{priorities.map(p => <option key={p}>{p}</option>)}</select><ChevronDown size={13}/></div></div>
}

function TicketTable({ tickets, loading, onOpen, compact = false }: { tickets: Ticket[]; loading: boolean; onOpen: (ticket: Ticket) => void; compact?: boolean }) {
  return <div className={`ticket-table-wrap ${compact ? 'compact-table' : ''}`}><table className="ticket-table"><thead><tr><th>SUBJECT</th><th>STATUS</th><th>PRIORITY</th>{!compact && <th>REQUESTER</th>}<th>UPDATED</th><th/></tr></thead><tbody>{loading ? Array.from({ length: compact ? 3 : 5 }, (_, i) => <tr key={i} className="loading-row"><td colSpan={compact ? 5 : 6}><span/></td></tr>) : tickets.length === 0 ? <tr><td colSpan={compact ? 5 : 6}><div className="empty-state"><div className="empty-icon"><Inbox size={20}/></div><strong>Nothing floating around here.</strong><span>Try another filter or create a new ticket.</span></div></td></tr> : tickets.map(ticket => <tr key={ticket.id} onClick={() => onOpen(ticket)} tabIndex={0} onKeyDown={e => { if (e.key === 'Enter') onOpen(ticket) }}><td><div className="ticket-subject"><div className={`ticket-mini-icon ${ticket.priority.toLowerCase()}`}><TicketIcon size={15}/></div><div className="ticket-title-block"><strong>{ticket.title}</strong><span><b>{ticket.number}</b><span className="sub-dot">·</span>{ticket.category}</span></div></div></td><td><StatusBadge status={ticket.status}/></td><td><PriorityBadge priority={ticket.priority}/></td>{!compact && <td><div className="requester-cell"><span className="requester-avatar">{initials(ticket.requesterName)}</span><span>{ticket.requesterName}</span></div></td>}<td><span className="updated-time">{relative(ticket.updatedAtUtc)}</span></td><td><button className="row-more" aria-label={`Open ${ticket.number}`} onClick={e => { e.stopPropagation(); onOpen(ticket) }}><ArrowUpRight size={15}/></button></td></tr>)}</tbody></table></div>
}

function StatusBadge({ status }: { status: TicketStatus }) {
  const cls = status === 'New' ? 'new' : status === 'Open' ? 'open' : status === 'InProgress' ? 'progress' : status === 'Resolved' ? 'resolved' : 'closed'
  return <span className={`status-badge ${cls}`}><i/>{niceStatus(status)}</span>
}
function PriorityBadge({ priority }: { priority: TicketPriority }) {
  return <span className={`priority-badge ${priority.toLowerCase()}`}><span>{priority === 'Urgent' ? '↟' : priority === 'High' ? '↑' : priority === 'Low' ? '↓' : '−'}</span>{priority}</span>
}
function QueueRow({ color, label, count, caption, onClick }: { color: string; label: string; count?: number; caption: string; onClick: () => void }) {
  return <button className="queue-row" onClick={onClick}><div className={`queue-marker ${color}`}/><div className="queue-row-copy"><strong>{label}</strong><span>{caption}</span></div><b>{count ?? '–'}</b><ArrowUpRight size={14}/></button>
}
function InlineError({ message }: { message: string }) { return <div className="inline-error"><CircleHelp size={16}/>{message}</div> }

function CreateTicketModal({ onClose, onCreate }: { onClose: () => void; onCreate: (values: { title: string; description: string; category: string; priority: TicketPriority }) => Promise<void> }) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [category, setCategory] = useState(categories[0])
  const [priority, setPriority] = useState<TicketPriority>('Medium')
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const submit = async (event: FormEvent) => { event.preventDefault(); setSaving(true); setError(''); try { await onCreate({ title, description, category, priority }) } catch (e) { setError(e instanceof Error ? e.message : 'Could not create ticket') } finally { setSaving(false) } }
  return <div className="modal-backdrop" onMouseDown={e => { if (e.target === e.currentTarget) onClose() }}><section className="create-modal" role="dialog" aria-modal="true" aria-labelledby="create-title"><div className="modal-header"><div><div className="modal-kicker">LET'S GET IT SORTED</div><h2 id="create-title">Raise a ticket<span>.</span></h2><p>Give us a little context and we’ll take it from there.</p></div><button className="icon-button modal-close" onClick={onClose} aria-label="Close"><X size={18}/></button></div><form onSubmit={submit} className="create-form"><label>What can we help with?<input autoFocus value={title} onChange={e => setTitle(e.target.value)} placeholder="A short, clear summary" maxLength={160} required minLength={5}/></label><label>Tell us a bit more<textarea value={description} onChange={e => setDescription(e.target.value)} placeholder="What happened? What did you expect? Any details help…" rows={4} maxLength={5000} required minLength={10}/><small>{description.length}/5000</small></label><div className="form-two-col"><label>Category<select value={category} onChange={e => setCategory(e.target.value)}>{categories.map(c => <option key={c}>{c}</option>)}</select></label><label>Priority<select value={priority} onChange={e => setPriority(e.target.value as TicketPriority)}>{priorities.map(p => <option key={p}>{p}</option>)}</select></label></div>{error && <div className="form-error">{error}</div>}<div className="modal-footer"><span><ShieldCheck size={15}/> Private to your account & support</span><button className="primary-button" type="submit" disabled={saving}>{saving ? <span className="spinner"/> : <Send size={15}/>}Send ticket</button></div></form></section></div>
}

function TicketDrawer({ ticket, loading, error, agents, session, onClose, onRefresh, onToast }: { ticket?: TicketDetail; loading: boolean; error: string; agents: Agent[]; session: Session; onClose: () => void; onRefresh: () => Promise<void>; onToast: (message: string) => void }) {
  const [comment, setComment] = useState('')
  const [internal, setInternal] = useState(false)
  const [status, setStatus] = useState<TicketStatus>('Open')
  const [statusNote, setStatusNote] = useState('')
  const [assignee, setAssignee] = useState('')
  const [busy, setBusy] = useState(false)
  const [tab, setTab] = useState<'conversation' | 'history'>('conversation')
  const [closureNote, setClosureNote] = useState('')
  const isAgent = session.roles.some(role => role === 'Agent' || role === 'Admin')
  const isAdmin = session.roles.includes('Admin')
  useEffect(() => { if (ticket) { setStatus(ticket.status); setAssignee(ticket.assigneeId ?? '') } }, [ticket?.id, ticket?.status, ticket?.assigneeId])
  const closureState = ticket?.history.find(item => item.field === 'Closure request')?.newValue
  const closurePending = closureState === 'Pending'
  const doAction = async (action: () => Promise<unknown>, message: string) => {
    setBusy(true)
    try {
      await action()
      onToast(message)
      // The API response confirms persistence. Refresh queries in the background so slow query refetches don't leave the button spinning.
      void onRefresh().catch(() => onToast('Saved successfully, but refreshing the ticket view failed. Reopen the ticket to reload it.'))
    } catch (e) {
      onToast(e instanceof Error ? e.message : 'Action failed')
    } finally {
      setBusy(false)
    }
  }
  const postComment = async (event: FormEvent) => { event.preventDefault(); if (!ticket || !comment.trim()) return; await doAction(() => api.addComment(ticket.id, comment.trim(), internal), internal ? 'Internal note added' : 'Reply sent'); setComment('') }
  return <div className="drawer-backdrop" onMouseDown={e => { if (e.target === e.currentTarget) onClose() }}><aside className="ticket-drawer" role="dialog" aria-modal="true" aria-label="Ticket details"><div className="drawer-top"><div className="drawer-brand"><div className="drawer-ticket-icon"><TicketIcon size={16}/></div><span>Ticket workspace</span></div><button className="icon-button" onClick={onClose} aria-label="Close details"><X size={18}/></button></div>{loading ? <div className="drawer-loading"><span className="spinner"/>Loading ticket details…</div> : error ? <div className="drawer-load-error">{error}</div> : ticket && <><div className="drawer-ticket-head"><div className="drawer-ticket-number">{ticket.number}<span className="sub-dot">·</span>{ticket.category}</div><h2>{ticket.title}</h2><div className="drawer-badges"><StatusBadge status={ticket.status}/><PriorityBadge priority={ticket.priority}/></div><p className="drawer-description">{ticket.description}</p><div className="drawer-meta"><div className="requester-avatar big">{initials(ticket.requesterName)}</div><div><strong>{ticket.requesterName}</strong><span>Requested {dateShort(ticket.createdAtUtc)}</span></div><div className="drawer-meta-separator"/><div><span className="meta-label">LAST UPDATED</span><strong>{relative(ticket.updatedAtUtc)}</strong></div></div></div>{isAgent && <div className="agent-controls"><label>Ticket status<select value={status} disabled={busy} onChange={e => setStatus(e.target.value as TicketStatus)}>{[ticket.status, ...allowedNextStatuses(ticket.status, isAdmin)].filter((value, index, all) => all.indexOf(value) === index).map(s => <option key={s} value={s}>{niceStatus(s)}</option>)}</select></label><label>Assigned to<select value={assignee} disabled={busy} onChange={e => { const value = e.target.value; setAssignee(value); if (value !== assignee) void doAction(() => api.assign(ticket.id, value), value ? 'Ticket assignment saved' : 'Ticket unassigned') }}><option value="">Unassigned</option>{agents.map(agent => <option value={agent.id} key={agent.id}>{agent.displayName}</option>)}</select></label><button className="apply-status" disabled={busy || status === ticket.status} onClick={() => void doAction(() => api.updateStatus(ticket.id, status, statusNote || undefined), status === 'Resolved' ? 'Ticket marked resolved. Customer confirmation requested.' : `Status changed to ${niceStatus(status)}`)}>{busy ? 'Saving…' : 'Update status'}<ArrowRight size={14}/></button>{status !== ticket.status && <input className="status-note" value={statusNote} onChange={e => setStatusNote(e.target.value)} placeholder="Optional note about this change"/>}</div>}{isAgent && ticket.status === 'Resolved' && closurePending && <section className="closure-status-note"><ShieldCheck size={17}/><div><strong>Waiting for requester confirmation</strong><p>This ticket stays Resolved until the customer confirms it is fixed. Only the requester can close it.</p></div></section>}{!isAgent && ticket.requesterId === session.userId && ticket.status === 'Resolved' && closurePending && <section className="closure-confirmation-card"><div className="closure-card-icon"><ShieldCheck size={19}/></div><div className="closure-card-copy"><h3>Is your issue resolved?</h3><p>Support marked this ticket as resolved. If everything works, confirm closure. If you still need help, we'll reopen the ticket for you.</p></div><label className="closure-note-label">Optional note <textarea value={closureNote} onChange={e => setClosureNote(e.target.value)} rows={2} placeholder="Add details if you still need help" disabled={busy}/></label><div className="closure-actions"><button className="closure-confirm-button" disabled={busy} onClick={() => void doAction(() => api.respondToClosure(ticket.id, true), 'Thanks for confirming. Your ticket is now closed.')}>{busy ? 'Saving…' : 'Confirm & close ticket'}</button><button className="closure-reopen-button" disabled={busy} onClick={() => void doAction(() => api.respondToClosure(ticket.id, false, closureNote.trim() || undefined), 'Ticket reopened. Support will continue working on it.')}>I still need help</button></div></section>}<div className="drawer-tabs"><button className={tab === 'conversation' ? 'selected' : ''} onClick={() => setTab('conversation')}><MessageCircle size={15}/>Conversation <span>{ticket.comments.length}</span></button><button className={tab === 'history' ? 'selected' : ''} onClick={() => setTab('history')}><FileClock size={15}/>Change history</button></div>{tab === 'conversation' ? <div className="conversation-area"><div className="conversation-date">TICKET ACTIVITY</div>{ticket.comments.length === 0 ? <div className="conversation-empty"><div className="conversation-empty-icon"><MessageCircle size={20}/></div><strong>The conversation starts here.</strong><span>Send a reply to keep things moving.</span></div> : ticket.comments.map(item => <article className={`comment-card ${item.authorId === session.userId ? 'own-comment' : ''} ${item.isInternal ? 'internal-comment' : ''}`} key={item.id}><div className="comment-avatar">{initials(item.authorName)}</div><div className="comment-content"><div className="comment-head"><strong>{item.authorName}</strong>{item.isInternal && <span className="internal-label"><ShieldCheck size={11}/> INTERNAL</span>}<time>{dateLong(item.createdAtUtc)}</time></div><p>{item.body}</p></div></article>)}{isAgent && <div className="visibility-toggle"><button className={!internal ? 'active' : ''} onClick={() => setInternal(false)}><MessageCircle size={13}/>Public reply</button><button className={internal ? 'active internal' : ''} onClick={() => setInternal(true)}><ShieldCheck size={13}/>Internal note</button></div>}{ticket.status === 'Closed' ? <div className="closed-ticket-note"><CheckCheck size={16}/><span>This ticket is closed. If the issue returns, create a new ticket to contact support.</span></div> : <form className="reply-form" onSubmit={postComment}><textarea value={comment} onChange={e => setComment(e.target.value)} rows={3} placeholder={internal ? 'Leave a note for your team…' : 'Write your reply…'} required minLength={1}/><div className="reply-form-foot"><span>{internal ? 'Only agents and admins can see this.' : 'Keep it clear, kind, and helpful.'}</span><button type="submit" className="send-reply" disabled={busy || !comment.trim()}><Send size={14}/>Send reply</button></div></form>}</div> : <div className="history-area">{ticket.history.length === 0 ? <div className="conversation-empty"><FileClock size={20}/><strong>No changes yet.</strong></div> : <div className="history-list">{ticket.history.map(entry => <div className="history-entry" key={entry.id}><div className="history-rail"><span/></div><div className="history-copy"><div><strong>{entry.actorName}</strong><time>{dateLong(entry.changedAtUtc)}</time></div><p><b>{entry.field}</b>{entry.oldValue && <> changed from <em>{entry.oldValue}</em></>} {entry.newValue && <>to <em>{entry.newValue}</em></>}</p></div></div>)}</div>}</div>}<div className="drawer-bottom-note"><ShieldCheck size={15}/> Ticket activity is recorded to keep everyone in sync.</div></>}</aside></div>
}

function TeamView({ users, loading, onRoleChange }: { users: UserRecord[]; loading: boolean; onRoleChange: (id: string, role: Role) => void }) {
  const [query, setQuery] = useState('')
  const filtered = users.filter(user => `${user.displayName} ${user.email}`.toLowerCase().includes(query.toLowerCase()))
  return <><div className="page-heading row-heading"><div><div className="eyebrow"><span className="eyebrow-line"/>ACCESS & GOVERNANCE</div><h1>Team & access<span className="heading-period">.</span></h1><p className="page-subtitle">The right people, with the right keys.</p></div><div className="team-counter"><div className="team-counter-icon"><Users size={18}/></div><div><strong>{users.length}</strong><span>MEMBERS</span></div></div></div><div className="team-intro panel"><div className="team-intro-icon"><ShieldCheck size={21}/></div><div><h2>Access, with intention.</h2><p>Permissions are enforced by the API. Role claims are refreshed against the database on each authenticated request, so changes take effect immediately.</p></div><div className="team-role-legend"><span className="role-pill role-admin">Admin</span><span className="role-pill role-agent">Agent</span><span className="role-pill role-user">User</span></div></div><div className="panel team-panel"><div className="panel-heading team-panel-heading"><div><h2>People in your workspace</h2><p>Manage support responsibilities and access levels.</p></div><div className="team-search"><Search size={15}/><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Find a teammate"/></div></div><div className="team-table-wrap"><table className="team-table"><thead><tr><th>MEMBER</th><th>ROLE</th><th>JOINED</th><th>ACCESS CONTROL</th></tr></thead><tbody>{loading ? <tr><td colSpan={4}>Loading team…</td></tr> : filtered.map(user => <tr key={user.id}><td><div className="team-person"><span className="team-avatar">{initials(user.displayName)}</span><span><strong>{user.displayName}</strong><small>{user.email}</small></span></div></td><td><span className={`role-pill role-${(user.roles[0] ?? 'user').toLowerCase()}`}>{user.roles[0] ?? 'User'}</span></td><td className="team-joined">{dateShort(user.createdAtUtc)}</td><td><div className="role-select-wrap"><select value={user.roles[0] ?? 'User'} onChange={e => onRoleChange(user.id, e.target.value as Role)} aria-label={`Change role for ${user.displayName}`}><option value="User">User</option><option value="Agent">Agent</option><option value="Admin">Admin</option></select><ChevronDown size={12}/></div></td></tr>)}</tbody></table>{!loading && !filtered.length && <div className="empty-state"><strong>No teammates found.</strong><span>Try searching a different name or email.</span></div>}</div></div><div className="bottom-note team-security-note"><div className="bottom-note-icon"><ShieldCheck size={16}/></div><span><strong>Keep admin access intentional.</strong> Agents can work tickets. Only admins can change roles or grant elevated access.</span><span className="note-spark">✦</span></div></>
}
