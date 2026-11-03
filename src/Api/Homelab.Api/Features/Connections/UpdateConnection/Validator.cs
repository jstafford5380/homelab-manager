using FastEndpoints;
using FluentValidation;

namespace Homelab.Api.Features.Connections.UpdateConnection;

public sealed class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(request => request.Input).NotNull().WithMessage("A JSON request body is required.");
    }
}
