using DigitalPreservation.CommonApiClient;
using DigitalPreservation.Common.Model.PreservationApi;

namespace Preservation.API.Tests.PreservationApiClient;

public class QueryBuilderTests
{
    [Fact]
    public void MakeQueryString_Includes_PageSize_WhenItIsOne()
    {
        var queryString = QueryBuilder.MakeQueryString(new DepositQuery { PageSize = 1 });

        queryString.Should().Contain("PageSize=1");
    }

    [Fact]
    public void MakeQueryString_Omits_Page_WhenItIsOne()
    {
        var queryString = QueryBuilder.MakeQueryString(new DepositQuery { Page = 1 });

        queryString.Should().NotContain("Page=1");
    }

    [Fact]
    public void MakeQueryString_Includes_Page_WhenItIsTwo()
    {
        var queryString = QueryBuilder.MakeQueryString(new DepositQuery { Page = 2 });

        queryString.Should().Contain("Page=2");
    }
}
