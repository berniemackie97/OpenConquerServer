namespace OpenConquer.Application.Characters.Login;

public readonly record struct CharacterLoginResolution
{
    private CharacterLoginResolution(CharacterLoginRoute route, CharacterLoginProfile? profile)
    {
        Route = route;
        Profile = profile;
    }

    public CharacterLoginRoute Route { get; }
    public CharacterLoginProfile? Profile { get; }

    public static CharacterLoginResolution CharacterCreation()
    {
        return new CharacterLoginResolution(CharacterLoginRoute.CharacterCreation, null);
    }

    public static CharacterLoginResolution ExistingCharacter(CharacterLoginProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new CharacterLoginResolution(CharacterLoginRoute.ExistingCharacter, profile);
    }
}
