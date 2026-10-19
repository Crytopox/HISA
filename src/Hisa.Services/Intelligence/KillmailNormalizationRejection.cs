namespace Hisa.Services.Intelligence;

public enum KillmailNormalizationRejection
{
    MalformedJson = 0,
    InvalidEnvelope = 1,
    MissingKillmailId = 2,
    MissingSolarSystem = 3,
    MissingTimestamp = 4,
    MissingVictim = 5,
    MissingVictimShip = 6
}
