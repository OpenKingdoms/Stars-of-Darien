// CardOverrideTests.cs - a card model hides the piece it replaces, its
// inactive twin, and the original's flat logo plates, and nothing else.
using NUnit.Framework;
using OpenKingdomsUnity.Game.World;

namespace OpenKingdomsUnity.Tests
{
    public class CardOverrideTests
    {
        [Test]
        public void ACardHidesItsPieceAndTwin()
        {
            var c = new CardOverride { ReplacesPiece = "VerLode" };
            Assert.IsTrue(c.Hides("VerLode"));
            Assert.IsTrue(c.Hides("verlode_off"));
            Assert.IsTrue(c.Hides("VerLodeoff"));
            Assert.IsFalse(c.Hides("base"));
            Assert.IsFalse(new CardOverride().Hides("VerLode_logo"), "no card, nothing hidden");
        }

        // The lodestones' logo plates, as the original models name them.
        [TestCase("VerLode_logo")]
        [TestCase("tarlodelogo")]
        [TestCase("aramanalogo")]
        [TestCase("logopoly")]
        [TestCase("logoplate")]
        public void ACardHidesTheLogoPlates(string piece)
        {
            var c = new CardOverride { ReplacesPiece = "zonlode" };
            Assert.IsTrue(c.Hides(piece));
        }

        [Test]
        public void ACardKeepsTheOtherCards()
        {
            var c = new CardOverride { ReplacesPiece = "zonlode" };
            Assert.IsFalse(c.Hides("skull1"));
            Assert.IsFalse(c.Hides("flag"));
        }
    }
}
