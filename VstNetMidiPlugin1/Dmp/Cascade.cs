using Jacobi.Vst.Framework;
using Jacobi.Vst.Core;
using System.Collections.Generic;
using System;

namespace MidiCascade.Dmp
{
	internal sealed class Cascade
	{
		private readonly byte[] MINOR = { 2, 1, 2, 2, 1, 2, 2 };
		private readonly byte[] MAJOR = { 2, 2, 1, 2, 2, 2, 1 };

		private readonly int[] C_MAJOR_MIDI = { 0, 2, 4, 5, 7, 9, 11 }; //todo: this is derivative from MAJOR
		private readonly int[] A_MINOR_MIDI = { 9, 11, 0, 2, 4, 5, 7 };
		private readonly string[] NAMES = { "Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "sol#", "La", "La#", "Si" };


		private static readonly string ParameterCategoryName = "Cascade";

		private Plugin _plugin;

		public Cascade(Plugin plugin)
		{
			_plugin = plugin;

			// todo: into class
			PressedNotes = new VstMidiEvent[200];
			ProcessedQuarters = new int[200];
			TimeInfos = new VstTimeInfo[200];

			InitializeParameters();

			_plugin.Opened += new System.EventHandler(Plugin_Opened);
		}

		private void Plugin_Opened(object sender, System.EventArgs e)
		{
			CascadeMgr.HostAutomation = _plugin.Host.GetInstance<IVstHostAutomation>();

			_plugin.Opened -= new System.EventHandler(Plugin_Opened);
		}

		public VstParameterManager CascadeMgr { get; private set; }

		private void InitializeParameters()
		{
			// all parameter definitions are added to a central list.
			var parameterInfos = _plugin.PluginPrograms.ParameterInfos;

			// retrieve the category for all delay parameters.
			var paramCategory =
				_plugin.PluginPrograms.GetParameterCategory(ParameterCategoryName);

			// delay time parameter
			var paramInfo = new VstParameterInfo();
			paramInfo.Category = paramCategory;
			paramInfo.CanBeAutomated = true;
			paramInfo.Name = "Down/Up";
			paramInfo.Label = "D/U";
			paramInfo.ShortLabel = "D/U";
			paramInfo.MinInteger = 0;
			paramInfo.MaxInteger = 1;
			paramInfo.LargeStepFloat = 1.0f;
			paramInfo.SmallStepFloat = 1.0f;
			paramInfo.StepFloat = 1.0f;
			paramInfo.DefaultValue = 1.0f;
			CascadeMgr = new VstParameterManager(paramInfo);
			VstParameterNormalizationInfo.AttachTo(paramInfo);

			parameterInfos.Add(paramInfo);
		}

		private int[] ProcessedQuarters;
		private VstMidiEvent[] PressedNotes;
		private VstTimeInfo[] TimeInfos;

		private IEnumerable<VstEvent> CreateCascadeNotes(VstMidiEvent inEvent, int quartersPassed, VstTimeInfo timeInfo)
		{
			//Log("CreateCascadeNotes");
			var origNote = inEvent.Data[1] % 12;
			//Log("origNote" + origNote);
			var step = Array.IndexOf(A_MINOR_MIDI, origNote);
			var note = inEvent.Data[1];
			if (step == -1)
			{
				//Log("rounding");
				note++; //rounding into scale
				step = Array.IndexOf(A_MINOR_MIDI, (origNote + 1) % 12);
			}
			var upDown = CascadeMgr.CurrentValue > 0.5 ? 1 : -1;
			//Log("doing " + note + " " + quartersPassed);
			for (int x = 0; x < quartersPassed; x++)
			{
				//Log("stepping " + MINOR[step]);
				
				//todo: up or down
				note += (byte)(MINOR[upDown == 1 ? step : ((step+6)%7)] * upDown);
				step = (step + 7 + upDown) % 7;
			}

			var name = NAMES[note % 12];
			Log("step" + name);


			var delta = (int)(TimeInfos[inEvent.Data[1]].SamplePosition + spb * quartersPassed - timeInfo.SamplePosition);
			Log("delta " + delta);
			//outData[1] pitch
			//outData[2] volume
			var outDataOn = new byte[] { 
				0x90, 
				note,
				inEvent.Data[2],
				0
			};

			var outEventOn = new VstMidiEvent(
				delta,
				0,
				0,
				outDataOn,
				0,
				0);

			var outDataOff = new byte[] { 
				0x80, 
				note,
				0,
				0
			};

			var outEventOff = new VstMidiEvent(
				delta + (int)spb, // quarter length
				0,
				0,
				outDataOff,
				0,
				0);

			return new VstEvent[] { outEventOn, outEventOff };
		}

		private void Log(string s)
		{
			_plugin.PluginEditor.Log(s);
		}

		double spb;
		public IEnumerable<VstEvent> CreateNotes(VstTimeInfo timeInfo)
		{
			spb = timeInfo.SampleRate * 60.0 / timeInfo.Tempo / 2; // 8th
			// todo: maybe need to shorten the initial note to quarter
			var outEvents = new List<VstEvent>();
			for (int i = 0; i < 200; i++)
				if (PressedNotes[i] != null)
				{

					var quartersPassed = HowManyQuartersPassed(PressedNotes[i], timeInfo);
					if (ProcessedQuarters[i] != quartersPassed) 
					{
						Log("quartersPassed" + quartersPassed);
						ProcessedQuarters[i] = quartersPassed; // avoid duplicates
						var newNotes = CreateCascadeNotes(PressedNotes[i], quartersPassed + 1, timeInfo);

						outEvents.AddRange(newNotes); // todo: will this come in Process for recursion?
					}
				}
			return outEvents;
		}

		private int HowManyQuartersPassed(VstMidiEvent vstMidiEvent, VstTimeInfo timeInfo)
		{
			//var str = string.Format("{0} {1} {2} {3}", vstMidiEvent.Data[0],vstMidiEvent.Data[1],vstMidiEvent.Data[2],vstMidiEvent.Data[3]);
			var presstime = TimeInfos[vstMidiEvent.Data[1]];
			var samplesPassed = (timeInfo.SamplePosition - presstime.SamplePosition);
			var quartersPassed = samplesPassed / spb;
			//var str = string.Format("{0} {1}", vstMidiEvent.Data[1], quartersPassed); 
			//Log("DeltaFrames " + str);
			return (int)quartersPassed;
		}

		private VstTimeInfo Clone(VstTimeInfo t)
		{
			return new VstTimeInfo
			{
				BarStartPosition= t.BarStartPosition,
				CycleEndPosition = t.CycleEndPosition,
				CycleStartPosition = t.CycleStartPosition,
				Flags = t.Flags,
				NanoSeconds = t.NanoSeconds,
				PpqPosition = t.PpqPosition,
				SamplePosition = t.SamplePosition,
				SampleRate = t.SampleRate,
				SamplesToNearestClock = t.SamplesToNearestClock,
				SmpteFrameRate = t.SmpteFrameRate,
				SmpteOffset = t.SmpteOffset,
				Tempo = t.Tempo,
				TimeSignatureDenominator = t.TimeSignatureDenominator,
				TimeSignatureNumerator = t.TimeSignatureNumerator,
			};
		}
		public VstMidiEvent ProcessEvent(VstMidiEvent inEvent, VstTimeInfo timeInfo)
		{
			if (!MidiHelper.IsNoteOff(inEvent.Data) && !MidiHelper.IsNoteOn(inEvent.Data))
				return inEvent;
			var note = inEvent.Data[1];
			//Log("DeltaFrames" + inEvent.DeltaFrames);
			if (MidiHelper.IsNoteOn(inEvent.Data))
			{
				PressedNotes[note] = inEvent;
				TimeInfos[note] = Clone(timeInfo);
				ProcessedQuarters[note] = -1;
				Log("IsNoteOn " + note);
			}
			if (MidiHelper.IsNoteOff(inEvent.Data))
			{
				Log("IsNoteOff " + note);
				PressedNotes[note] = null;
				TimeInfos[note] = null;
				ProcessedQuarters[note] = 100000; // todo: amirite?
			}

			return inEvent;
		}
	}
}
