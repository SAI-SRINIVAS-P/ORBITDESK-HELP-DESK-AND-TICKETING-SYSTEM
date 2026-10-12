using FluentValidation;
using HelpDesk.Api.DTOs;
namespace HelpDesk.Api.Validators;
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest> {
    public RegisterRequestValidator() {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a non-alphanumeric character.");
    }
}
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest> {
    public LoginRequestValidator() { RuleFor(x => x.Email).NotEmpty().EmailAddress(); RuleFor(x => x.Password).NotEmpty(); RuleFor(x => x.LoginAs).Must(x => x is "User" or "Admin").When(x => !string.IsNullOrWhiteSpace(x.LoginAs)).WithMessage("Choose either User or Admin sign-in."); }
}
