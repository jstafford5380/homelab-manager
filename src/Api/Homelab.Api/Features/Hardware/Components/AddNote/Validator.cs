using FastEndpoints;
using FluentValidation;

namespace Homelab.Api.Features.Hardware.Components.AddNote;

public sealed class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(request => request.Input).NotNull().WithMessage("A JSON request body is required.");
    }
}
