using Xunit;
using MTGViewer.Data;


namespace MTGViewer.Tests.Data
{
    public class LocationTests
    {
        
        [Fact]
        public void IsShared_NoOwner_ReturnsTrue()
        {
            var locaton = new Location("Test Location");

            bool isShared = locaton.IsShared;

            Assert.True(isShared);
        }
    }
}