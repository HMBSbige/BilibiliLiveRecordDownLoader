using BilibiliLiveRecordDownLoader.Models;
using System.Reflection;

namespace BilibiliLiveRecordDownLoader.JsonConverters;

public class GlobalConfigConverter : IgnoreValueConverter<Config>
{
	protected override bool ShouldWrite(PropertyInfo propertyInfo, object? value)
	{
		switch (propertyInfo.Name)
		{
			case nameof(Config.MainDir):
			{
				if (Equals(value, Config.DefaultMainDir))
				{
					return false;
				}
				break;
			}
			case nameof(Config.Rooms):
			{
				if (value is not List<RoomStatus> rooms || rooms.Count == 0)
				{
					return false;
				}

				break;
			}
		}
		return base.ShouldWrite(propertyInfo, value);
	}
}
