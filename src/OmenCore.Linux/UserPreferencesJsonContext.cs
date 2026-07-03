using System.Text.Json.Serialization;
using OmenCore.Linux.Config;

namespace OmenCore.Linux;

[JsonSerializable(typeof(UserHardwarePreferences))]
[JsonSerializable(typeof(KeyboardLightingPreferences))]
[JsonSerializable(typeof(FanControlPreferences))]
[JsonSerializable(typeof(SavedFanPreset))]
[JsonSerializable(typeof(SavedFanCurvePoint))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class UserPreferencesJsonContext : JsonSerializerContext;
