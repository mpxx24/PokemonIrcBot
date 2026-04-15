using NUnit.Framework;
using PokemonIrcBot.Services;

namespace PokemonIrcBot.Tests;

[TestFixture]
public class TypeChartTests
{
    [TestCase("fire",     new[] { "grass" },           2.0)]
    [TestCase("fire",     new[] { "water" },           0.5)]
    [TestCase("water",    new[] { "fire" },            2.0)]
    [TestCase("water",    new[] { "grass" },           0.5)]
    [TestCase("electric", new[] { "water" },           2.0)]
    [TestCase("electric", new[] { "ground" },          0.0)]
    [TestCase("normal",   new[] { "ghost" },           0.0)]
    [TestCase("fighting", new[] { "normal" },          2.0)]
    [TestCase("poison",   new[] { "grass" },           2.0)]
    [TestCase("ice",      new[] { "dragon" },          2.0)]
    [TestCase("dragon",   new[] { "fairy" },           0.0)]
    [TestCase("normal",   new[] { "normal" },          1.0)]
    public void GetMultiplier_SingleDefenderType_ReturnsExpected(
        string attackType, string[] defenderTypes, double expected)
    {
        var result = TypeChart.GetMultiplier(attackType, defenderTypes);

        Assert.That(result, Is.EqualTo(expected).Within(0.001));
    }

    [Test]
    public void GetMultiplier_DualType_MultipliesComponents()
    {
        // Fire vs Grass/Poison: 2× (grass) * 1× (poison) = 2×
        var result = TypeChart.GetMultiplier("fire", ["grass", "poison"]);
        Assert.That(result, Is.EqualTo(2.0).Within(0.001));
    }

    [Test]
    public void GetMultiplier_DualTypeDoubleWeak_ReturnsFour()
    {
        // Rock vs Fire/Flying: 2× (fire) * 2× (flying) = 4×
        var result = TypeChart.GetMultiplier("rock", ["fire", "flying"]);
        Assert.That(result, Is.EqualTo(4.0).Within(0.001));
    }

    [Test]
    public void GetMultiplier_DualTypeImmunity_ReturnsZero()
    {
        // Normal vs Ghost/Steel: 0× (ghost) * 0.5× (steel) = 0×
        var result = TypeChart.GetMultiplier("normal", ["ghost", "steel"]);
        Assert.That(result, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void GetMultiplier_UnknownType_ReturnsOne()
    {
        var result = TypeChart.GetMultiplier("shadow", ["normal"]);
        Assert.That(result, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void GetMultiplier_EmptyDefenderTypes_ReturnsOne()
    {
        var result = TypeChart.GetMultiplier("fire", []);
        Assert.That(result, Is.EqualTo(1.0).Within(0.001));
    }
}
