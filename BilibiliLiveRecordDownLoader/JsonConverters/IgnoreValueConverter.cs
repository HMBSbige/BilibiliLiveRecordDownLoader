using BilibiliLiveRecordDownLoader.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BilibiliLiveRecordDownLoader.JsonConverters
{
	public abstract class IgnoreValueConverter<T> : JsonConverter<T>
	{
		private static readonly Lazy<IEnumerable<PropertyInfo>> PropertiesLazy = new(
				() => typeof(T).GetPropertiesExcludeJsonIgnore()
		);

		private static IEnumerable<PropertyInfo> Properties => PropertiesLazy.Value;

		protected virtual bool ShouldWrite(PropertyInfo propertyInfo, object? value)
		{
			var jsonIgnore = propertyInfo.GetCustomAttribute<JsonIgnoreAttribute>();
			if (jsonIgnore is not null)
			{
				switch (jsonIgnore.Condition)
				{
					case JsonIgnoreCondition.Never:
					{
						break;
					}
					case JsonIgnoreCondition.Always:
					{
						return false;
					}
					case JsonIgnoreCondition.WhenWritingDefault:
					{
						var defaultValueAttribute = propertyInfo.GetCustomAttribute<DefaultValueAttribute>();
						if (defaultValueAttribute is not null)
						{
							return !Equals(value, defaultValueAttribute.Value);
						}

						var defaultValue = DefaultValue.Get(propertyInfo.PropertyType);

						if (Equals(value, defaultValue))
						{
							return false;
						}
						break;
					}
					case JsonIgnoreCondition.WhenWritingNull:
					{
						if (value is null)
						{
							return false;
						}
						break;
					}
				}
			}

			return true;
		}

		public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType is not JsonTokenType.StartObject)
			{
				goto JSonError;
			}

			var res = Activator.CreateInstance<T>();
			while (reader.Read())
			{
				if (reader.TokenType is JsonTokenType.EndObject)
				{
					return res;
				}

				if (reader.TokenType is JsonTokenType.PropertyName)
				{
					var propertyName = reader.GetString();
					if (propertyName is null)
					{
						goto JSonError;
					}

					reader.Read();

					var property = typeToConvert.GetProperty(propertyName);
					if (property?.SetMethod is null || !property.SetMethod.IsPublic)
					{
						continue;
					}

					if (Utils.Utils.ShouldIgnore(property))
					{
						continue;
					}

					var value = JsonSerializer.Deserialize(ref reader, property.PropertyType, options);
					property.SetValue(res, value);
				}
			}
JSonError:
			throw new JsonException();
		}

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();

			var properties = Properties;

			if (options.IgnoreReadOnlyProperties)
			{
				properties = properties.Where(p => p.SetMethod is not null && p.SetMethod.IsPublic);
			}

			foreach (var property in properties)
			{
				var v = property.GetValue(value);

				if (!ShouldWrite(property, v))
				{
					continue;
				}

				writer.WritePropertyName(property.Name);
				JsonSerializer.Serialize(writer, v, property.PropertyType, options);
			}

			writer.WriteEndObject();
		}
	}
}
