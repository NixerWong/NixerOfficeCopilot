using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace OfficeCopilot.Host.Excel.Tools
{
    internal static class ExcelToolJsonSerializer
    {
        public static T Deserialize<T>(string json) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                var value = serializer.ReadObject(stream) as T;
                return value ?? new T();
            }
        }

        public static string Serialize<T>(T value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}