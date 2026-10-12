using FluentValidation;
using Microsoft.AspNetCore.Mvc;
namespace HelpDesk.Api.Controllers;
[ApiController]
public abstract class ValidationControllerBase : ControllerBase {
    protected async Task<IActionResult?> ValidateRequestAsync<T>(T request, IValidator<T> validator, CancellationToken token) {
        var result = await validator.ValidateAsync(request, token);
        if (result.IsValid) return null;
        return BadRequest(new { errors = result.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }) });
    }
}
