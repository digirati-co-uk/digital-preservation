using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace DigitalPreservation.UI.ViewComponents;

public class PagerViewComponent : ViewComponent
{
    public const int DefaultPageSize = 10;
    public const int DefaultWindow = 7;
    public const int DefaultEnds = 3;

    /// <summary>
    /// Pass a <paramref name="key"/> when a page shows more than one pager (e.g. Search.cshtml's
    /// Fedora and Deposit tabs), so each gets its own cached model instead of colliding on the
    /// per-request cache key (issue #388 item 4.1).
    /// </summary>
    public Task<IViewComponentResult> InvokeAsync(PagerValues values, string? key = null)
    {
        if (values.HideForSinglePage && values.Total <= values.Size)
        {
            // Nothing to page
            return Task.FromResult<IViewComponentResult>(Content(String.Empty));
        }

        var cacheKey = key ?? nameof(PagerViewComponent);

        // Store the model in the request for duplicated pagers
        if (HttpContext.Items[cacheKey] is not PagerModel model)
        {
            model = BuildModel(values, Request.Path);
            HttpContext.Items[cacheKey] = model;
        }

        return Task.FromResult<IViewComponentResult>(View(model));
    }

    private static PagerModel BuildModel(PagerValues values, PathString path)
    {
        int pages = values.Total / values.Size;
        if (values.Total % values.Size > 0) pages++;
        var model = new PagerModel
        {
            Links = [],
            TotalItems = values.Total,
            TotalPages = pages,
            CurrentPage = values.Index
        };

        // we don't want to loop through the pages - there could be 100,000 of them
        // instead we want the first few, then an ellipsis,
        // then the current "window", then another ellipsis, then the end
        // but only if there are enough pages to justify this.
        // prev | 1 2 3 ... 45 46 [47] 48 49 ... 654 655 656 | next
        if (pages < (2 * values.Ends) + values.Window + 2)
        {
            AddLinkRange(model, path, 1, pages, values);
        }
        else
        {
            AddWindowedLinks(model, path, pages, values);
        }

        return model;
    }

    private static void AddWindowedLinks(PagerModel model, PathString path, int pages, PagerValues values)
    {
        int windowStart = values.Index - (values.Window / 2);
        AddLinkRange(model, path, 1, values.Ends, values);

        if (windowStart > values.Ends + 2)
        {
            // we're not into the window yet, add an ellipsis
            model.Links.Add(new Link {Page = null});
        }
        else
        {
            // we're into the window already
            AddLinkToModel(model, path, values.Ends + 1, values);
        }

        windowStart = Math.Max(values.Ends + 2, windowStart);
        int tail = pages - values.Ends - values.Window;
        if (windowStart >= tail)
        {
            // just run through to the end
            AddLinkRange(model, path, tail, pages, values);
        }
        else
        {
            AddLinkRange(model, path, windowStart, Math.Min(windowStart + values.Window, pages) - 1, values);

            if (model.Links[^1].Page < pages - values.Ends)
            {
                model.Links.Add(new Link {Page = null});
            }

            AddLinkRange(model, path, pages - values.Ends + 1, pages, values);
        }
    }

    private static void AddLinkRange(PagerModel model, PathString path, int from, int to, PagerValues values)
    {
        // from/to are derived from values.Index, which carries the unsanitized "page" query
        // string value - clamp against model.TotalPages (the actual page count) so a crafted
        // page number can never drive this loop beyond the real number of pages.
        var lowerBound = Math.Max(from, 1);
        var upperBound = Math.Min(to, model.TotalPages);
        for (int linkPage = lowerBound; linkPage <= upperBound; linkPage++)
        {
            AddLinkToModel(model, path, linkPage, values);
        }
    }

    private static void AddLinkToModel(PagerModel model, PathString path, int linkPage, PagerValues values)
    {
        var link = GetLink(path, linkPage, values); 
        model.Links.Add(link);
        if (linkPage == values.Index - 1) model.Previous = link;
        if (linkPage == values.Index + 1) model.Next = link;
    }


    private static Link GetLink(PathString path, int linkPage, PagerValues values)
    {
        Dictionary<string, StringValues> qsDict;
        if (values.QueryStringDict != null)
        {
            qsDict = values.QueryStringDict.ToDictionary(
                x => x.Key, x => x.Value);
        }
        else
        {
            qsDict = new Dictionary<string, StringValues>();
        }
        
        if (linkPage > 1)
        {
            qsDict["page"] = linkPage.ToString();
        }
        else
        {
            qsDict.Remove("page");
        }
        
        if (values.Size != DefaultPageSize)
        {
            qsDict["pageSize"] = values.Size.ToString();
        }
        else
        {
            qsDict.Remove("pageSize");
        }

        // This is not used in Leeds
        if (values.OrderBy.HasText())
        {
            if (values.Descending)
            {
                qsDict["orderByDescending"] = values.OrderBy;
            }
            else
            {
                qsDict["orderBy"] = values.OrderBy;
            }
        }
        qsDict.RemoveEmptyKeys();
        
        var link = new Link
        {
            Current = linkPage == values.Index,
            Href = QueryHelpers.AddQueryString(path, qsDict),
            Page = linkPage
        };
        return link;
    }
}

public class PagerModel
{
    public int TotalPages { get; set; }
    public List<Link> Links { get; set; } = [];
    public Link? Previous { get; set; }
    public Link? Next { get; set; }
    public int TotalItems { get; set; }
    public int CurrentPage { get; set; }
}

public record Link
{
    public int? Page { get; set; }
    public string? Href { get; set; }
    public bool Current { get; set; }
}


public class PagerValues
{
    public PagerValues(){}

    // new version where there is an existing query string
    public PagerValues(QueryString queryString, int total, int pageSize, bool hideForSinglePage = true)
    {
        Total = total;
        HideForSinglePage = hideForSinglePage;
        QueryStringDict = QueryHelpers.ParseQuery(queryString.ToString());
        Index = 1;
        if (QueryStringDict.TryGetValue("page", out var page))
        {
            Index = int.TryParse(page, out var result) ? result : 1;
        }
        Size = pageSize;
        if (Size <= 0)
        {
            Size = PagerViewComponent.DefaultPageSize;
            if (QueryStringDict.TryGetValue("pageSize", out var pageSizeQs))
            {
                Size = int.TryParse(pageSizeQs, out var result) ? result : PagerViewComponent.DefaultPageSize;
            }
        }

        Window = PagerViewComponent.DefaultWindow;
        Ends = PagerViewComponent.DefaultEnds;
    }
    
    // version from protagonist where there are no other params
    public PagerValues(
        int total, int index, int size,
        string? orderBy = null, bool descending = false,
        int window = PagerViewComponent.DefaultWindow, int ends = PagerViewComponent.DefaultEnds)
    {
        Total = total;
        Index = index;
        Size = size;
        OrderBy = orderBy;
        Descending = descending;
        Window = window;
        Ends = ends;
    }
    
    public int Total { get; set; }
    public int Index { get; set; }
    public int Size { get; set; }
    public string? OrderBy { get; set; }
    public bool Descending { get; set; }
    public int Window { get; set; }
    public int Ends { get; set; }
    public bool HideForSinglePage { get; set; }
    
    public Dictionary<string, StringValues>? QueryStringDict;
}