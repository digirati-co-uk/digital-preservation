using DigitalPreservation.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace DigitalPreservation.UI.ViewComponents;

public class PagerTwoViewComponent : ViewComponent
{
    public const int DefaultPageSize = 10;
    public const int DefaultWindow = 7;
    public const int DefaultEnds = 3;

    public Task<IViewComponentResult> InvokeAsync(PagerValues values)
    {
        if (values.HideForSinglePage && values.Total <= values.Size)
        {
            // Nothing to page
            return Task.FromResult<IViewComponentResult>(Content(String.Empty));
        }

        // Store the model in the request for duplicated pagers
        if (HttpContext.Items[nameof(PagerTwoViewComponent)] is not PagerModel model)
        {
            var path = Request.Path;
            int pages = values.Total / values.Size;
            if (values.Total % values.Size > 0) pages++;
            model = new PagerModel
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
                for (int linkPage = 1; linkPage <= pages; linkPage++)
                {
                    AddLinkToModel(model, path, linkPage, values);
                }
            }
            else
            {
                int windowStart = values.Index - (values.Window / 2);
                for (int linkPage = 1; linkPage <= values.Ends; linkPage++)
                {
                    AddLinkToModel(model, path, linkPage, values);
                }

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
                    for (int linkPage = tail; linkPage <= pages; linkPage++)
                    {
                        AddLinkToModel(model, path, linkPage, values);
                    }
                }
                else
                {
                    for (int linkPage = windowStart; linkPage < Math.Min(windowStart + values.Window, pages); linkPage++)
                    {
                        AddLinkToModel(model, path, linkPage, values);
                    }

                    if (model.Links.Last().Page < pages - values.Ends)
                    {
                        model.Links.Add(new Link {Page = null});
                    }

                    for (int linkPage = pages - values.Ends + 1; linkPage <= pages; linkPage++)
                    {
                        AddLinkToModel(model, path, linkPage, values);
                    }
                }
            }

            HttpContext.Items[nameof(PagerTwoViewComponent)] = model;
        }

        return Task.FromResult<IViewComponentResult>(View(model));
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
