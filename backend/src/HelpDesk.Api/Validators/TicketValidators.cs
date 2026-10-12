using FluentValidation;
using HelpDesk.Api.DTOs;
namespace HelpDesk.Api.Validators;
public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest> {
    public CreateTicketRequestValidator() {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Description).NotEmpty().MinimumLength(10).MaximumLength(10000);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Priority).IsInEnum();
    }
}
public sealed class AddCommentRequestValidator : AbstractValidator<AddCommentRequest> {
    public AddCommentRequestValidator() { RuleFor(x => x.Body).NotEmpty().MaximumLength(5000); }
}
public sealed class UpdateTicketStatusRequestValidator : AbstractValidator<UpdateTicketStatusRequest> {
    public UpdateTicketStatusRequestValidator() { RuleFor(x => x.Status).IsInEnum(); RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null); }
}
