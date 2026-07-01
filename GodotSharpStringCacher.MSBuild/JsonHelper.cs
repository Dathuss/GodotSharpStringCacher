using System.IO;
using System.Runtime.Serialization.Json;

/// <summary>
/// Helper for JSON serializing using <see cref="System.Runtime.Serialization.DataContractAttribute"/>s.<br/>
/// Decrepit technology, but has the advantage of being available in .NET Standard 2.0.
/// </summary>
public static class JsonHelper
{
	public static void Serialize<T>(T obj, Stream stream)
	{
		DataContractJsonSerializer serializer = new(typeof(T));
		serializer.WriteObject(stream, obj);
	}

	public static T Deserialize<T>(Stream stream)
	{
		DataContractJsonSerializer serializer = new(typeof(T));
		return (T)serializer.ReadObject(stream);
	}
}
