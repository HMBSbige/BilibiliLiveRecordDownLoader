using BilibiliLiveRecordDownLoader.Models;
using ReactiveUI;
using System.Reactive;

namespace BilibiliLiveRecordDownLoader.Services
{
	public class MessageInteractions
	{
		public Interaction<RoomStatus, Unit> ShowLiveStatus { get; } = new();
	}
}
