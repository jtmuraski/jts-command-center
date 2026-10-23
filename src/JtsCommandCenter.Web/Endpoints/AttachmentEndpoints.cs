using JtsCommandCenter.Core.Projects;

namespace JtsCommandCenter.Web.Endpoints;

public static class AttachmentEndpoints
{
    /// <summary>
    /// GET /attachments/{id}: serves an uploaded image. The storage container is private, so this is
    /// the only way images are reached; once sign-in exists, this endpoint must require it too.
    /// </summary>
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/attachments/{id:guid}", async (Guid id, IAttachmentService attachments, HttpContext http, CancellationToken cancellationToken) =>
        {
            var content = await attachments.OpenAsync(id, cancellationToken);
            if (content is null)
            {
                return Results.NotFound();
            }

            var headers = http.Response.Headers;
            // An id always refers to the same bytes, so browsers can keep it.
            headers.CacheControl = "private, max-age=31536000, immutable";
            // Belt and braces: never let a browser treat the bytes as anything but the image type.
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";

            // Results.Stream disposes the stream once the response is written.
            return Results.Stream(content.Content, content.Attachment.ContentType);
        });

        return endpoints;
    }
}
