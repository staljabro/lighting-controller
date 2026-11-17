namespace LightingController.Core;

public sealed class FixturePatch
{
    private readonly List<PatchedFixture> _fixtures = [];

    public IReadOnlyList<PatchedFixture> Fixtures => _fixtures;

    public void Add(PatchedFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (_fixtures.Any(existing => existing.Id == fixture.Id))
        {
            throw new InvalidOperationException($"Fixture ID {fixture.Id} is already patched.");
        }

        var overlap = _fixtures.FirstOrDefault(existing =>
            existing.Universe == fixture.Universe &&
            fixture.Address <= existing.LastAddress &&
            fixture.LastAddress >= existing.Address);
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"Fixture '{fixture.Name}' overlaps '{overlap.Name}' on universe {fixture.Universe}.");
        }

        _fixtures.Add(fixture);
        _fixtures.Sort((left, right) => left.Id.CompareTo(right.Id));
    }

    public bool Remove(int fixtureId) => _fixtures.RemoveAll(fixture => fixture.Id == fixtureId) > 0;

    public PatchedFixture? Find(int fixtureId) => _fixtures.FirstOrDefault(fixture => fixture.Id == fixtureId);
}
