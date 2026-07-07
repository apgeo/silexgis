// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;

namespace SilexGis.Api.Common;

/// <summary>List envelope for paged endpoints.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems);

public static class Paging
{
    public const int MaxPageSize = 500;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, MaxPageSize));

    public static async Task<PagedResult<TDto>> ToPagedAsync<TEntity, TDto>(
        this IQueryable<TEntity> query,
        int page,
        int pageSize,
        Func<TEntity, TDto> map,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<TDto>([.. items.Select(map)], page, pageSize, total);
    }
}
