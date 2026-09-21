namespace UltraLibrarianImporter.UI.Services.Interfaces;

/// <summary>
/// Where the provider API keys are being kept, as <see cref="IConfigService"/> last found it.
/// </summary>
/// <param name="IsPersistent">
/// True when the keys are saved in the OS credential store; false when that store could not be
/// used and the keys are held in memory for this session only.
/// </param>
/// <param name="Message">A sentence for the Settings dialog saying which of the two it is, and why.</param>
public sealed record SecretStorageStatus(bool IsPersistent, string Message);
