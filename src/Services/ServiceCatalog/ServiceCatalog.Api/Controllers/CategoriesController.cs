using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.ServiceCatalog.Application.Abstractions;
namespace Zumaris.ServiceCatalog.Api.Controllers;

public sealed record CategoryResponse(int Id, string Name, string? IconUrl);

[ApiController]
[Authorize]
[Route("api/categories")]
public sealed class CategoriesController(ICategoryRepository categories, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> Get(CancellationToken cancellationToken)
    {
        var prefix = (configuration["CategoryIcons:PublicPath"] ?? "/service-catalog/api/categories/icons").TrimEnd('/');
        return Ok((await categories.ListAsync(cancellationToken)).Select(category =>
            new CategoryResponse(category.Id, category.Name, CatalogIconFiles.IsValidName(category.Icon)
                ? prefix + "/" + Uri.EscapeDataString(category.Icon!) : null)).ToArray());
    }
}
