using DigitalPreservation.Common.Model.ChangeDiscovery;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests.ChangeDiscovery;

/// <summary>
/// OrderedCollection.For and OrderedCollectionPage.For replace the two copies of this
/// collection/page-building logic that Preservation API and Storage API each hand-rolled for their
/// own Activity Stream (issue #388 item 2.8). These pin the shared logic directly: an empty stream
/// still gets a real page 1 as both first and last (not page 0, which Storage API's own copy used
/// to compute and which no page handler would ever answer), totalPages rounds up correctly, and
/// prev/next appear only where another page actually exists.
/// </summary>
public class OrderedCollectionFactoriesTests
{
    private static readonly Uri CollectionId = new("https://preservation.test/activity/collection");
    private static Uri PageUri(int page) => new($"https://preservation.test/activity/pages/{page}");

    public class OrderedCollectionForTests
    {
        [Fact]
        public void An_Empty_Stream_Still_Gets_A_Real_Page_1_As_First_And_Last()
        {
            var collection = OrderedCollection.For(CollectionId, PageUri, totalItems: 0);

            collection.TotalItems.Should().Be(0);
            collection.First!.Id.Should().Be(PageUri(1));
            collection.Last!.Id.Should().Be(PageUri(1));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(100, 1)]
        [InlineData(101, 2)]
        [InlineData(250, 3)]
        public void TotalPages_Rounds_Up_To_Cover_Every_Item(int totalItems, int expectedLastPage)
        {
            var collection = OrderedCollection.For(CollectionId, PageUri, totalItems);

            collection.Last!.Id.Should().Be(PageUri(expectedLastPage));
        }

        [Fact]
        public void The_Context_Is_Set()
        {
            OrderedCollection.For(CollectionId, PageUri, 0).Context.Should().NotBeNullOrEmpty();
        }
    }

    public class OrderedCollectionPageForTests
    {
        [Fact]
        public void Page_1_Of_One_Has_Neither_Prev_Nor_Next()
        {
            var page = OrderedCollectionPage.For(CollectionId, PageUri, page: 1, totalItems: 1, items: []);

            page.Prev.Should().BeNull();
            page.Next.Should().BeNull();
            page.StartIndex.Should().Be(0);
        }

        [Fact]
        public void A_Middle_Page_Has_Both_Prev_And_Next()
        {
            var page = OrderedCollectionPage.For(
                CollectionId, PageUri, page: 2, totalItems: OrderedCollectionPage.DefaultPageSize * 3, items: []);

            page.Prev!.Id.Should().Be(PageUri(1));
            page.Next!.Id.Should().Be(PageUri(3));
        }

        [Fact]
        public void The_Last_Page_Has_No_Next()
        {
            var page = OrderedCollectionPage.For(
                CollectionId, PageUri, page: 2, totalItems: OrderedCollectionPage.DefaultPageSize * 2, items: []);

            page.Next.Should().BeNull();
        }

        [Fact]
        public void PartOf_Points_At_The_Collection()
        {
            var page = OrderedCollectionPage.For(CollectionId, PageUri, page: 1, totalItems: 0, items: []);

            page.PartOf!.Id.Should().Be(CollectionId);
        }
    }
}
