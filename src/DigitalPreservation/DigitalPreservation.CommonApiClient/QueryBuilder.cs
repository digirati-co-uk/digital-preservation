using System.Collections.Specialized;
using System.Web;
using DigitalPreservation.Common.Model.PreservationApi;
using DigitalPreservation.Utils;

namespace DigitalPreservation.CommonApiClient;

public class QueryBuilder
{
    public static string MakeQueryString(DepositQuery? query)
    {
        if (query == null)
        {
            return string.Empty;
        }
        var queryString = BuildBase(query);
        
        if (query.PreservedBy != null)
        {  
            queryString.Add(nameof(query.PreservedBy), query.PreservedBy);
        }
        if (query.PreservedAfter.HasValue)
        {  
            queryString.Add(nameof(query.PreservedAfter), query.PreservedAfter.Value.ToString("s"));
        }
        if (query.PreservedBefore.HasValue)
        {  
            queryString.Add(nameof(query.PreservedBefore), query.PreservedBefore.Value.ToString("s"));
        }
        if (query.ExportedBy != null)
        {  
            queryString.Add(nameof(query.ExportedBy), query.ExportedBy);
        }
        if (query.ExportedAfter.HasValue)
        {  
            queryString.Add(nameof(query.ExportedAfter), query.ExportedAfter.Value.ToString("s"));
        }
        if (query.ExportedBefore.HasValue)
        {  
            queryString.Add(nameof(query.ExportedBefore), query.ExportedBefore.Value.ToString("s"));
        }

        if (query.ArchivalGroupPath.HasText())
        {
            queryString.Add(nameof(query.ArchivalGroupPath), query.ArchivalGroupPath);
        }
        if (query.Status.HasText())
        {
            queryString.Add(nameof(query.Status), query.Status);
        }
        if (query.ShowAll is true)
        {
            queryString.Add(nameof(query.ShowAll), "true");
        }
        if (query.ShowForm is true)
        {
            queryString.Add(nameof(query.ShowForm), "true");
        }
        
        return queryString.ToString() ?? string.Empty;
    }


    private static NameValueCollection BuildBase(QueryBase queryBase)
    {
        var queryString = HttpUtility.ParseQueryString(string.Empty);

        if (queryBase.CreatedBy != null)
        {  
            queryString.Add(nameof(queryBase.CreatedBy), queryBase.CreatedBy);
        }
        if (queryBase.CreatedAfter.HasValue)
        {  
            queryString.Add(nameof(queryBase.CreatedAfter), queryBase.CreatedAfter.Value.ToString("s"));
        }
        if (queryBase.CreatedBefore.HasValue)
        {  
            queryString.Add(nameof(queryBase.CreatedBefore), queryBase.CreatedBefore.Value.ToString("s"));
        }
        if (queryBase.LastModifiedBy != null)
        {  
            queryString.Add(nameof(queryBase.LastModifiedBy), queryBase.LastModifiedBy);
        }
        if (queryBase.LastModifiedAfter.HasValue)
        {  
            queryString.Add(nameof(queryBase.LastModifiedAfter), queryBase.LastModifiedAfter.Value.ToString("s"));
        }
        if (queryBase.LastModifiedBefore.HasValue)
        {  
            queryString.Add(nameof(queryBase.LastModifiedBefore), queryBase.LastModifiedBefore.Value.ToString("s"));
        }
        if (queryBase.OrderBy.HasText())
        {
            queryString.Add(nameof(queryBase.OrderBy), queryBase.OrderBy);
        }
        if (queryBase.Ascending.HasValue && queryBase.Ascending.Value)
        {
            queryString.Add(nameof(queryBase.Ascending), "true");
        }
        
        // Returns "key1=value1&key2=value2", all URL-encoded
        return queryString;
    }
}