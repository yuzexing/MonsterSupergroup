using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AstralShift.Helpers
{
	internal class NewtonsoftPrivateResolver : DefaultContractResolver
	{
		protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
		{
			JsonProperty jsonProperty = base.CreateProperty(member, memberSerialization);
			if (!jsonProperty.Writable)
			{
				bool writable = (member as PropertyInfo)?.GetSetMethod(nonPublic: true) != null;
				jsonProperty.Writable = writable;
			}
			return jsonProperty;
		}
	}
}
