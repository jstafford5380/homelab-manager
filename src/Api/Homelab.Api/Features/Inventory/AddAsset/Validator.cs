using FastEndpoints;
using FluentValidation;

namespace Homelab.Api.Features.Inventory.AddAsset;

public sealed class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(request => request.Input).NotNull().WithMessage("A JSON request body is required.");
    }
}
