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
        return Ok((await categories.ListAsync(cancellationToken)).Select(ToResponse).ToArray());
    }

    [AllowAnonymous]
    [HttpGet("{id:int:min(1)}")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : Ok(ToResponse(category));
    }

    private CategoryResponse ToResponse(Zumaris.ServiceCatalog.Domain.Entities.Category category)
    {
        var prefix = (configuration["CategoryIcons:PublicPath"] ?? "/service-catalog/api/categories/icons").TrimEnd('/');
        return new CategoryResponse(category.Id, category.Name, CatalogIconFiles.IsValidName(category.Icon)
            ? prefix + "/" + Uri.EscapeDataString(category.Icon!) : null);
    }
}
