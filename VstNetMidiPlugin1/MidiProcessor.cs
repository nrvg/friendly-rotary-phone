using System.Linq;
using Jacobi.Vst.Core;
using Jacobi.Vst.Framework;
using Jacobi.Vst.Framework.Plugin;
using MidiCascade.Dmp;

namespace MidiCascade
{
	/// <summary>
	/// This object performs midi processing for your plugin.
	/// </summary>
	internal sealed class MidiProcessor : IVstMidiProcessor, IVstPluginMidiSource
	{
		private Plugin _plugin;

		/// <summary>
		/// Constructs a new Midi Processor.
		/// </summary>
		/// <param name="plugin">Must not be null.</param>
		public MidiProcessor(Plugin plugin)
		{
			_plugin = plugin;
			Cascade = new Cascade(plugin);

			// for most hosts, midi output is expected during the audio processing cycle.
			SyncWithAudioProcessor = true;
		}

		internal Cascade Cascade { get; private set; }

		/// <summary>
		/// Gets or sets a value indicating to sync with audio processing.
		/// </summary>
		/// <remarks>
		/// False: will output midi to the host in the MidiProcessor.
		/// True: will output midi to the host in the AudioProcessor.
		/// </remarks>
		public bool SyncWithAudioProcessor { get; set; }

		public int ChannelCount
		{
			get { return 16; }
		}

		/// <summary>
		/// Midi events are received from the host on this method.
		/// </summary>
		/// <param name="events">A collection with midi events. Never null.</param>
		/// <remarks>
		/// Note that some hosts will only receieve midi events during audio processing.
		/// See also <see cref="IVstPluginAudioProcessor"/>.
		/// </remarks>
		public void Process(VstEventCollection events)
		{
			CurrentEvents = events;
			//_plugin.PluginEditor.Log("Midi Process");
			if (!SyncWithAudioProcessor)
			{
				ProcessCurrentEvents();
			}
		}

		// cache of events (for when syncing up with the AudioProcessor).
		public VstEventCollection CurrentEvents { get; private set; }

		public void ProcessCurrentEvents()
		{
			//_plugin.PluginEditor.Log("ProcessCurrentEvents");

			// a plugin must implement IVstPluginMidiSource or this call will throw an exception.
			var midiHost = _plugin.Host.GetInstance<IVstMidiProcessor>();

			// always expect some hosts not to support this.
			if (midiHost != null)
			{
				var outEvents = new VstEventCollection();

				var someCommands = _plugin.Host.GetInstance<IVstHostCommands20>();
				var timeInfo = someCommands.GetTimeInfo(
					VstTimeInfoFlags.PpqPositionValid | VstTimeInfoFlags.BarStartPositionValid | VstTimeInfoFlags.TempoValid);
				if (CurrentEvents != null)
				// NOTE: other types of events could be in the collection!
					foreach (VstEvent evnt in CurrentEvents)
					{
						switch (evnt.EventType)
						{
							case VstEventTypes.MidiEvent:
								var midiEvent = (VstMidiEvent)evnt;

								midiEvent = Cascade.ProcessEvent(midiEvent, timeInfo);

								//_plugin.PluginEditor.Log("tempo" + x.Tempo);

								outEvents.Add(midiEvent);
								break;
							default:
								// non VstMidiEvent
								outEvents.Add(evnt);
								break;
						}
					}

				outEvents.AddRange(Cascade.CreateNotes(timeInfo));

				midiHost.Process(outEvents);
			}

			// Clear the cache, we've processed the events.
			CurrentEvents = null;
		}

		#region IVstPluginMidiSource Members

		int IVstPluginMidiSource.ChannelCount
		{
			get { return 16; }
		}

		#endregion
	}
}
