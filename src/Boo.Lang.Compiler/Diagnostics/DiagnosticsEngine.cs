using System;
using Boo.Lang.Compiler;

namespace Boo.Lang.Compiler.Diagnostics
{
	// Delegate for the notification of new diagnostics
	public delegate void DiagnosticEventHandler(DiagnosticLevel level, Diagnostic diag);
	public delegate void ContextEventHandler(CompilerContext context);
	public delegate void InputEventHandler(ICompilerInput input);


	/// <summary>
	/// Serves as main interface between the compiler and the diagnostics sub system.
	/// </summary>
	public class DiagnosticsEngine
	{
		/// <summary>
		/// Notify successfully consumed diagnostics
		/// </summary>
		public event DiagnosticEventHandler Handler;

		/// <summary>
		/// Notify a new diagnostics session
		/// </summary>
		public event ContextEventHandler OnStartContext;

		/// <summary>
		/// Notify a new file being analyzed
		/// </summary>
		public event InputEventHandler OnStartFile;


		public bool IgnoreAllWarnings {	get; set; }
		public bool WarningsAsErrors { get; set; }
		public bool ErrorsAsFatal { get; set; }   
		public bool SuppressAllDiagnostics { get; set; }
		public int ErrorLimit { get; set; }
		public int[] IgnoredCodes { get; set; }
		public int[] PromotedCodes { get; set; }

		public int NoteCount { get; set; }
		public int WarningCount { get; set; }
		public int ErrorCount { get; set; }
		public bool FatalOcurred { get; set; }

		public int Count {
			get { return ErrorCount + WarningCount + NoteCount + (FatalOcurred ? 1 : 0); }
		}

		public void StartContext(CompilerContext context)
		{
			if (null != OnStartContext)
				OnStartContext(context);
		}

		public void StartFile(ICompilerInput inp)
		{
			if (null != OnStartFile)
				OnStartFile(inp);
		}

		/// <summary>
		/// Reset error counters
		/// </summary>
		virtual public void Reset()
		{
			NoteCount = 0;
			WarningCount = 0;
			ErrorCount = 0;
			FatalOcurred = false;
		}

		/// <summary>
		/// Maps a diagnostic to a normalized severity level based on the configuration
		/// </summary>
		virtual protected DiagnosticLevel Map(Diagnostic diag)
		{
			if (FatalOcurred || SuppressAllDiagnostics)
				return DiagnosticLevel.Ignored;

			var level = diag.Level;

			if (null != IgnoredCodes && -1 != Array.IndexOf(IgnoredCodes, diag.Code))
				level = DiagnosticLevel.Ignored;

			if (null != PromotedCodes && -1 != Array.IndexOf(PromotedCodes, diag.Code)) 
			{
				switch (level) {
				case DiagnosticLevel.Ignored:
					level = DiagnosticLevel.Note;
					break;
				case DiagnosticLevel.Note:
					level = DiagnosticLevel.Warning;
					break;
				case DiagnosticLevel.Warning:
					level = DiagnosticLevel.Error;
					break;
				case DiagnosticLevel.Error:
					level = DiagnosticLevel.Fatal;
					break;
				}
			}

			if (WarningsAsErrors && level == DiagnosticLevel.Warning)
				level = DiagnosticLevel.Error;

			if (IgnoreAllWarnings && level == DiagnosticLevel.Warning)
				level = DiagnosticLevel.Ignored;

			if (ErrorsAsFatal && level == DiagnosticLevel.Error)
				level = DiagnosticLevel.Fatal;

			if (ErrorLimit != 0 && ErrorCount >= ErrorLimit)
				level = DiagnosticLevel.Ignored;

			return level;
		}


		/// <summary>
		/// Consume a diagnostic produced by the compiler to notify the configured
		/// handlers if needed.
		/// </summary>
		virtual public void Consume(Diagnostic diag)
		{
			var level = Map(diag);

			switch (level) {
			case DiagnosticLevel.Ignored:
				return;
			case DiagnosticLevel.Fatal:
				FatalOcurred = true;
				break;
			case DiagnosticLevel.Error:
				ErrorCount += 1;
				break;
			case DiagnosticLevel.Warning:
				WarningCount += 1;
				break;
			case DiagnosticLevel.Note:
				NoteCount += 1;
				break;
			}

			if (null != Handler)
				Handler(level, diag);
		}
	}
}