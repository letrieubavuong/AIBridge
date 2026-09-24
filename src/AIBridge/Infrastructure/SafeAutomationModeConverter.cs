using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIBridge.Models;

namespace AIBridge.Infrastructure;

public class SafeAutomationModeConverter : JsonConverter<AutomationMode>
{
    public override AutomationMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                if (!string.IsNullOrWhiteSpace(str) && Enum.TryParse<AutomationMode>(str, ignoreCase: true, out var result))
                {
                    return result;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out var val) && Enum.IsDefined(typeof(AutomationMode), val))
                {
                    return (AutomationMode)val;
                }
            }
        }
        catch
        {
            // Fallback safely to Manual
        }

        return AutomationMode.Manual;
    }

    public override void Write(Utf8JsonWriter writer, AutomationMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
