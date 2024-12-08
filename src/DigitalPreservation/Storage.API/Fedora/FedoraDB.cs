using Dapper;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Utils;
using Npgsql;
using Storage.API.Fedora.Model;

namespace Storage.API.Fedora;

public class FedoraDB
{
    private readonly string? connectionString;
    private ILogger<FedoraDB> logger;
    private readonly Converters? converters;

    private int basicContainer;
    private int fedoraArchivalGroup;
    private int leedsArchivalGroup;
    private int binary;

    private readonly string? containmentQuery;

    static FedoraDB()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
    
    public FedoraDB(
        Converters converters,
        string? connectionString,
        ILogger<FedoraDB> logger)
    {
        this.connectionString = connectionString;
        this.logger = logger;
        if (connectionString.IsNullOrWhiteSpace())
        {
            logger.LogInformation("Fedora connection string is empty, marking DB not available.");
            return;
        }
        this.converters = converters;
        try
        {
            var searchTypeIds = GetTypesInClause();
            if (searchTypeIds.HasText())
            {
                Available = true;
                containmentQuery =
                    " select containment.fedora_id, created, modified, content_size, mime_type, rdf_type_id " + 
                    " from containment inner join simple_search on containment.fedora_id=simple_search.fedora_id " + 
                    " left join search_resource_rdf_type on simple_search.id=search_resource_rdf_type.resource_id " + 
                    " where containment.parent=@parent and rdf_type_id in " + searchTypeIds + " " +
                    " order by containment.fedora_id ";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fedora connection string is provided, but not able to read Fedora DB, marking DB not available.");
        }
    }

    private string GetTypesInClause()
    {
        var knownTypes = GetConnection().Query<RdfType>("SELECT id, rdf_type_uri FROM search_rdf_type").ToList();
        
        basicContainer = knownTypes.Single(t => t.RdfTypeUri == "http://www.w3.org/ns/ldp#BasicContainer").Id;
        fedoraArchivalGroup = knownTypes.Single(t => t.RdfTypeUri == "http://fedora.info/definitions/v4/repository#ArchivalGroup").Id;
        leedsArchivalGroup = knownTypes.Single(t => t.RdfTypeUri == "http://purl.org/dc/dcmitype/Collection").Id;
        binary = knownTypes.Single(t => t.RdfTypeUri == "http://fedora.info/definitions/v4/repository#Binary").Id;

        return $"({basicContainer},{fedoraArchivalGroup},{leedsArchivalGroup},{binary})";
    }

    public bool Available { get; private set; }

    private NpgsqlConnection GetConnection()
    {
        return new NpgsqlConnection(connectionString);
    }

    public async Task<Container?> GetPopulatedContainer(Uri fedoraUri)
    {
        var parent = converters!.GetFedoraDbId(fedoraUri);
        var containedResources = await GetConnection()
            .QueryAsync<SearchRowWithType>(containmentQuery!, new { parent });
        string fedoraId = "";
        List<SearchRowWithType> collapsed = [];
        SearchRowWithType? current = null;
        foreach (var rowWithType in containedResources)
        {
            if (rowWithType.FedoraId != fedoraId)
            {
                current = rowWithType;
                collapsed.Add(current);
                fedoraId = rowWithType.FedoraId;
            }
            current!.RdfTypeIds.Add(rowWithType.RdfTypeId);
        }
        
        var container = new Container();
        foreach (var rowWithType in collapsed)
        {
            if (rowWithType.RdfTypeIds.Contains(leedsArchivalGroup))
            {
                container.Containers.Add(new ArchivalGroup
                {
                    Id = converters.RepositoryUriFromDbId(rowWithType.FedoraId),
                    Created = rowWithType.Created,
                    LastModified = rowWithType.Modified
                });
            }
            else if (rowWithType.RdfTypeIds.Contains(basicContainer))
            {
                container.Containers.Add(new Container
                {
                    Id = converters.RepositoryUriFromDbId(rowWithType.FedoraId),
                    Created = rowWithType.Created,
                    LastModified = rowWithType.Modified
                });
            }
            else if (rowWithType.RdfTypeIds.Contains(binary))
            {
                container.Binaries.Add(new Binary
                {
                    Id = converters.RepositoryUriFromDbId(rowWithType.FedoraId),
                    Created = rowWithType.Created,
                    LastModified = rowWithType.Modified,
                    ContentType = rowWithType.MimeType,
                    Size = rowWithType.ContentSize
                });
            }
            else
            {
                throw new NotSupportedException($"Fedora type {rowWithType.FedoraId} is not supported");
            }
        }
        return container;
    }
}

class RdfType
{
    public int Id { get; init; }
    public string? RdfTypeUri { get; init; }
}

class SearchRowWithType
{
    public required string FedoraId { get; set; }
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public int ContentSize { get; set; }
    public string? MimeType { get; set; }
    public int RdfTypeId { get; set; }
    public List<int> RdfTypeIds { get; set; } = [];
}