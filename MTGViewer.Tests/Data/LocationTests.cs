using Xunit;
using MTGViewer.Data;


namespace MTGViewer.Tests.Data
{
    public class LocationTests
    {
        [Fact]
        public void IsShared_NoOwner_ReturnsTrue()
        {
            var locaton = new Location("No owner location")
            {
                Owner = null
            };

            bool isShared = locaton.IsShared;

            Assert.True(isShared);
        }
    }
}