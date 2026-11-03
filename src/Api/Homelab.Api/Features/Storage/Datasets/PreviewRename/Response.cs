namespace Homelab.Api.Features.Storage.Datasets.PreviewRename;
public sealed record Response(string ReviewToken, DatasetRenamePreview From, DatasetRenamePreview To, string Warning);
