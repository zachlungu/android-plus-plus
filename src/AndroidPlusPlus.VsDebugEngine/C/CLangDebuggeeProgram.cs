﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.Debugger.Interop;
using AndroidPlusPlus.Common;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.VsDebugEngine
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class CLangDebuggeeProgram : IDebugProgram3
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    protected readonly CLangDebugger m_debugger;

    protected Dictionary <string, DebuggeeModule> m_debugModules;

    protected Dictionary <uint, DebuggeeThread> m_debugThreads;

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeProgram (CLangDebugger debugger, DebuggeeProgram debugProgram)
    {
      m_debugger = debugger;

      DebugProgram = debugProgram;

      IsRunning = false;

      m_debugModules = new Dictionary<string, DebuggeeModule> ();

      m_debugThreads = new Dictionary<uint, DebuggeeThread> ();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public DebuggeeProgram DebugProgram { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public bool IsRunning { get; protected set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public uint CurrentThreadId { get; set; }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void RefreshAllThreads ()
    {
      LoggingUtils.PrintFunction ();

      RefreshThread (0);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void RefreshThread (uint tid)
    {
      LoggingUtils.PrintFunction ();

      try
      {
        m_debugger.RunInterruptOperation (delegate (CLangDebugger debugger)
        {
          string command = string.Format ("-thread-info {0}", (tid == 0) ? "" : tid.ToString ());

          MiResultRecord resultRecord = m_debugger.GdbClient.SendCommand (command);

          MiResultRecord.RequireOk (resultRecord, command);

          if (!resultRecord.HasField ("threads"))
          {
            throw new InvalidOperationException ("-thread-info result missing 'threads' field");
          }

          MiResultValueList threadsValueList = (MiResultValueList) resultRecord ["threads"] [0];

          List<MiResultValue> threadsData = threadsValueList.List;

          bool refreshedProcesses = false;

          for (int i = threadsData.Count - 1; i >= 0; --i) // reported threads are in descending order.
          {
            uint id = threadsData [i] ["id"] [0].GetUnsignedInt ();

            CLangDebuggeeThread thread = GetThread (id);

            if (thread == null)
            {
              thread = AddThread (id);
            }

            if (thread.RequiresRefresh)
            {
              MiResultValue threadData = threadsData [i];

              if (!refreshedProcesses)
              {
                AndroidDevice hostDevice = DebugProgram.DebugProcess.NativeProcess.HostDevice;

                hostDevice.RefreshProcesses (DebugProgram.DebugProcess.NativeProcess.Pid);

                refreshedProcesses = true;
              }

              thread.Refresh (ref threadData);
            }
          }

          if (resultRecord.HasField ("current-thread-id"))
          {
            CurrentThreadId = resultRecord ["current-thread-id"] [0].GetUnsignedInt ();
          }
        });
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeModule AddModule (string moduleName, MiAsyncRecord asyncRecord)
    {
      LoggingUtils.PrintFunction ();

      if (string.IsNullOrWhiteSpace (moduleName))
      {
        throw new ArgumentNullException ("moduleName");
      }

      DebuggeeModule module = null;

      lock (m_debugModules)
      {
        if (!m_debugModules.TryGetValue (moduleName, out module))
        {
          module = new CLangDebuggeeModule (m_debugger.Engine, asyncRecord);

          m_debugModules.Add (moduleName, module);
        }
      }

      if (module != null)
      {
        m_debugger.Engine.Broadcast (new DebugEngineEvent.ModuleLoad (module as IDebugModule2, true), DebugProgram, null);

        if (module.SymbolsLoaded)
        {
          m_debugger.Engine.Broadcast (new DebugEngineEvent.BeforeSymbolSearch (module as IDebugModule3), DebugProgram, null);

          m_debugger.Engine.Broadcast (new DebugEngineEvent.SymbolSearch (module as IDebugModule3, module.Name), DebugProgram, null);
        }
      }

      return (CLangDebuggeeModule) module;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeModule GetModule (string moduleName)
    {
      LoggingUtils.PrintFunction ();

      if (string.IsNullOrWhiteSpace (moduleName))
      {
        throw new ArgumentNullException ("moduleName");
      }

      DebuggeeModule module = null;

      lock (m_debugModules)
      {
        m_debugModules.TryGetValue (moduleName, out module);
      }

      return (CLangDebuggeeModule) module;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public bool RemoveModule (string moduleName)
    {
      LoggingUtils.PrintFunction ();

      if (string.IsNullOrWhiteSpace (moduleName))
      {
        throw new ArgumentNullException ("moduleName");
      }

      DebuggeeModule module = null;

      lock (m_debugModules)
      {
        if (m_debugModules.TryGetValue (moduleName, out module))
        {
          m_debugModules.Remove (moduleName);
        }
      }

      if (module != null)
      {
        m_debugger.Engine.Broadcast (new DebugEngineEvent.ModuleLoad (module as IDebugModule2, false), DebugProgram, null);
      }

      return (module != null);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeThread AddThread (uint threadId)
    {
      LoggingUtils.PrintFunction ();

      DebuggeeThread thread = null;

      lock (m_debugThreads)
      {
        if (!m_debugThreads.TryGetValue (threadId, out thread))
        {
          thread = new CLangDebuggeeThread (m_debugger, this, threadId);

          m_debugThreads.Add (threadId, thread);
        }
      }

      if (thread != null)
      {
        m_debugger.Engine.Broadcast (new DebugEngineEvent.ThreadCreate (), DebugProgram, thread);
      }

      return (CLangDebuggeeThread) thread;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public bool RemoveThread (uint threadId, uint exitCode)
    {
      LoggingUtils.PrintFunction ();

      DebuggeeThread thread = null;

      lock (m_debugThreads)
      {
        if (m_debugThreads.TryGetValue (threadId, out thread))
        {
          m_debugThreads.Remove (threadId);
        }
      }

      if (thread != null)
      {
        m_debugger.Engine.Broadcast (new DebugEngineEvent.ThreadDestroy (exitCode), DebugProgram, thread);
      }

      return (thread != null);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public CLangDebuggeeThread GetThread (uint threadId)
    {
      LoggingUtils.PrintFunction ();

      DebuggeeThread thread = null;

      lock (m_debugThreads)
      {
        m_debugThreads.TryGetValue (threadId, out thread);
      }

      return (CLangDebuggeeThread) thread;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public Dictionary<uint, DebuggeeThread> GetThreads ()
    {
      LoggingUtils.PrintFunction ();

      return m_debugThreads;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public void SetRunning (bool isRunning)
    {
      LoggingUtils.PrintFunction ();

      IsRunning = isRunning;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IDebugProgram2 Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int Attach (IDebugEventCallback2 pCallback)
    {
      // 
      // Attaches to this program.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        m_debugger.Engine.Broadcast (new CLangDebuggerEvent.StartServer (m_debugger), DebugProgram, null);

        m_debugger.Engine.Broadcast (new CLangDebuggerEvent.AttachClient (m_debugger), DebugProgram, null);

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int CanDetach ()
    {
      LoggingUtils.PrintFunction ();

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int CauseBreak ()
    {
      LoggingUtils.PrintFunction ();

      try
      {
        m_debugger.Engine.Broadcast (new CLangDebuggerEvent.StopClient (m_debugger), DebugProgram, null);

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int Continue (IDebugThread2 pThread)
    {
      LoggingUtils.PrintFunction ();

      try
      {
        if (!IsRunning)
        {
          m_debugger.Engine.Broadcast (new CLangDebuggerEvent.ContinueClient (m_debugger), DebugProgram, null);
        }

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int Detach ()
    {
      LoggingUtils.PrintFunction ();

      try
      {
        m_debugger.Engine.Broadcast (new CLangDebuggerEvent.DetachClient (m_debugger), DebugProgram, null);

        m_debugger.Engine.Broadcast (new CLangDebuggerEvent.TerminateServer (m_debugger), DebugProgram, null);

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int EnumCodeContexts (IDebugDocumentPosition2 pDocPos, out IEnumDebugCodeContexts2 ppEnum)
    {
      // 
      // Enumerates the code contexts for a given position in a source file.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        string fileName;

        TEXT_POSITION [] startPos = new TEXT_POSITION [1];

        TEXT_POSITION [] endPos = new TEXT_POSITION [1];

        LoggingUtils.RequireOk (pDocPos.GetFileName (out fileName));

        LoggingUtils.RequireOk (pDocPos.GetRange (startPos, endPos));

        DebuggeeDocumentContext documentContext = new DebuggeeDocumentContext (m_debugger.Engine, fileName, startPos [0], endPos [0]);

        CLangDebuggeeCodeContext codeContext = CLangDebuggeeCodeContext.GetCodeContextForDocumentContext (m_debugger, documentContext);

        if (codeContext == null)
        {
          throw new InvalidOperationException ("Failed evaluating code-context for location.");
        }

        CLangDebuggeeCodeContext [] codeContexts = new CLangDebuggeeCodeContext [] { codeContext };

        ppEnum = new DebuggeeCodeContext.Enumerator (codeContexts);

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        ppEnum = null;

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int EnumCodePaths (string pszHint, IDebugCodeContext2 pStart, IDebugStackFrame2 pFrame, int fSource, out IEnumCodePaths2 ppEnum, out IDebugCodeContext2 ppSafety)
    {
      // 
      // Enumerates the code paths of this program.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        // 
        // Get the entire call-stack for the current thread, and enumerate.
        // 

        CLangDebuggeeStackFrame stackFrame = pFrame as CLangDebuggeeStackFrame;

        IDebugThread2 thread;

        LoggingUtils.RequireOk (stackFrame.GetThread (out thread));

        CLangDebuggeeThread stackFrameThread = thread as CLangDebuggeeThread;

        List<DebuggeeStackFrame> threadCallStack = stackFrameThread.StackTrace (uint.MaxValue);

        List <CODE_PATH> threadCodePaths = new List <CODE_PATH> ();

        for (int i = 0; i < threadCallStack.Count; ++i)
        {
          string frameName;

          IDebugCodeContext2 codeContext;

          DebuggeeStackFrame frame = threadCallStack [i] as DebuggeeStackFrame;

          LoggingUtils.RequireOk (frame.GetName (out frameName));

          LoggingUtils.RequireOk (frame.GetCodeContext (out codeContext));

          if (codeContext != null)
          {
            CODE_PATH codePath = new CODE_PATH ();

            codePath.bstrName = frameName;

            codePath.pCode = codeContext;

            threadCodePaths.Add (codePath);
          }
        }

        ppEnum = new DebuggeeProgram.EnumeratorCodePaths (threadCodePaths);

        ppSafety = null;

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        ppEnum = null;

        ppSafety = null;

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int EnumModules (out IEnumDebugModules2 ppEnum)
    {
      // 
      // Enumerates the modules that this program has loaded and is executing.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        List<IDebugModule2> modules = new List<IDebugModule2> ();

        foreach (DebuggeeModule module in m_debugModules.Values)
        {
          modules.Add (module as IDebugModule2);
        }

        ppEnum = new DebuggeeModule.Enumerator (modules);

        if (ppEnum == null)
        {
          throw new InvalidOperationException ();
        }

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        ppEnum = null;

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int EnumThreads (out IEnumDebugThreads2 ppEnum)
    {
      // 
      // Enumerates the threads that are running in this program.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        List<IDebugThread2> threads = new List<IDebugThread2> ();

        threads.AddRange (m_debugThreads.Values);

        ppEnum = new DebuggeeThread.Enumerator (threads);

        if (ppEnum == null)
        {
          throw new InvalidOperationException ();
        }

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        ppEnum = null;

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetDebugProperty (out IDebugProperty2 ppProperty)
    {
      // 
      // Gets program properties.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        throw new NotImplementedException ();
      }
      catch (NotImplementedException e)
      {
        LoggingUtils.HandleException (e);

        ppProperty = null;

        return DebugEngineConstants.E_NOTIMPL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int Execute ()
    {
      // 
      // Continues running this program from a stopped state. Any previous execution state is cleared.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        LoggingUtils.RequireOk (Continue (GetThread (CurrentThreadId)));

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetDisassemblyStream (enum_DISASSEMBLY_STREAM_SCOPE dwScope, IDebugCodeContext2 pCodeContext, out IDebugDisassemblyStream2 ppDisassemblyStream)
    {
      LoggingUtils.PrintFunction ();

      ppDisassemblyStream = new CLangDebuggeeDisassemblyStream (m_debugger, dwScope, pCodeContext);

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetENCUpdate (out object ppUpdate)
    {
      // 
      // Gets the Edit and Continue (ENC) update for this program.
      // A custom debug engine does not implement this method (it should always return E_NOTIMPL).
      // 

      LoggingUtils.PrintFunction ();

      ppUpdate = null;

      return DebugEngineConstants.E_NOTIMPL;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetEngineInfo (out string pbstrEngine, out Guid pguidEngine)
    {
      // 
      // Gets the name and identifier of the debug engine (DE) running a program.
      // 

      LoggingUtils.PrintFunction ();

      pguidEngine = DebugEngineGuids.guidDebugEngineID;

      pbstrEngine = DebugEngineGuids.GetEngineNameFromId (pguidEngine);

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetMemoryBytes (out IDebugMemoryBytes2 ppMemoryBytes)
    {
      // 
      // Gets the memory bytes for this program.
      // 

      LoggingUtils.PrintFunction ();

      if (m_debugger.NativeMemoryBytes != null)
      {
        ppMemoryBytes = m_debugger.NativeMemoryBytes;

        return DebugEngineConstants.S_OK;
      }

      ppMemoryBytes = null;

      return DebugEngineConstants.S_GETMEMORYBYTES_NO_MEMORY_BYTES;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetName (out string pbstrName)
    {
      // 
      // Gets the name of the program.
      // 

      LoggingUtils.PrintFunction ();

      pbstrName = DebugProgram.DebugProcess.NativeProcess.Name;

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetProcess (out IDebugProcess2 ppProcess)
    {
      // 
      // Gets the process that this program is running in.
      // 

      LoggingUtils.PrintFunction ();

      ppProcess = DebugProgram.DebugProcess;

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int GetProgramId (out Guid pguidProgramId)
    {
      // 
      // Gets a globally unique identifier for this program.
      // 

      LoggingUtils.PrintFunction ();

      pguidProgramId = DebugProgram.Guid;

      return DebugEngineConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int Step (IDebugThread2 pThread, enum_STEPKIND sk, enum_STEPUNIT Step)
    {
      // 
      // Performs a step.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        CLangDebuggeeThread thread = pThread as CLangDebuggeeThread;

        uint threadId;

        LoggingUtils.RequireOk (thread.GetThreadId (out threadId));

        GdbClient.StepType stepType = (GdbClient.StepType)Step;

        switch (sk)
        {
          case enum_STEPKIND.STEP_INTO:
          {
            m_debugger.GdbClient.StepInto (threadId, stepType, false);

            break;
          }
          case enum_STEPKIND.STEP_OVER:
          {
            m_debugger.GdbClient.StepOver (threadId, stepType, false);

            break;
          }
          case enum_STEPKIND.STEP_OUT:
          {
            m_debugger.GdbClient.StepOut (threadId, stepType, false);

            break;
          }
          case enum_STEPKIND.STEP_BACKWARDS:
          {
            throw new NotImplementedException ();
          }
        }

        return DebugEngineConstants.S_OK;
      }
      catch (NotImplementedException e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_NOTIMPL;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int Terminate ()
    {
      // 
      // Terminates this program.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        m_debugger.Engine.Broadcast (new CLangDebuggerEvent.TerminateClient (m_debugger), DebugProgram, null);

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int WriteDump (enum_DUMPTYPE DUMPTYPE, string pszDumpUrl)
    {
      // 
      // Writes a dump to a file.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        throw new NotImplementedException ();
      }
      catch (NotImplementedException e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_NOTIMPL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IDebugProgram3 Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int ExecuteOnThread (IDebugThread2 pThread)
    {
      // 
      // Executes the debugger program. The thread is returned to give the debugger information on which thread the user is viewing when executing the program.
      // 

      LoggingUtils.PrintFunction ();

      try
      {
        uint threadId;

        CLangDebuggeeThread thread = pThread as CLangDebuggeeThread;

        LoggingUtils.RequireOk (thread.GetThreadId (out threadId));

        string command = "-thread-select " + threadId;

        MiResultRecord resultRecord = m_debugger.GdbClient.SendCommand (command);

        MiResultRecord.RequireOk (resultRecord, command);

        CurrentThreadId = threadId;

        LoggingUtils.RequireOk (Execute ());

        return DebugEngineConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return DebugEngineConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
