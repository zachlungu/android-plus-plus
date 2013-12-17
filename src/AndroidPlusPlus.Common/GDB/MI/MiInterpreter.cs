﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.Common
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public class MiInterpreter
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static MiRecord ParseGdbOutputRecord (string streamOutput)
    {
      if (string.IsNullOrEmpty (streamOutput))
      {
        return null;
      }

      // 
      // Process any leading 'async-record' or 'result-record' token.
      // 

      int streamIndex = 0;

      if (streamOutput.StartsWith ("(gdb)"))
      {
        // 
        // GDB prompt. Waiting for input.
        // 

        return new MiPromptRecord ();
      }
      else if (streamOutput [streamIndex] == '~')
      {
        // 
        // Console stream record. Clears leading '~" and trailing '\\n"' characters.
        // 

        ++streamIndex;

        return new MiStreamRecord (MiStreamRecord.StreamType.Console, streamOutput.Substring (streamIndex + 1, (streamOutput.Length - (streamIndex + 1) - 3)));
      }
      else if (streamOutput [streamIndex] == '@')
      {
        // 
        // Target stream record.
        // 

        ++streamIndex;

        return new MiStreamRecord (MiStreamRecord.StreamType.Target, streamOutput.Substring (streamIndex + 1, (streamOutput.Length - (streamIndex + 1) - 3)));
      }
      else if (streamOutput [streamIndex] == '&')
      {
        // 
        // Log stream record.
        // 

        ++streamIndex;

        return new MiStreamRecord (MiStreamRecord.StreamType.Log, streamOutput.Substring (streamIndex + 1, (streamOutput.Length - (streamIndex + 1) - 3)));
      }
      else
      {
        // 
        // The following record types have associated key-pair data; identify the type and build a result collection.
        // 

        string recordData = streamOutput.Substring (streamIndex);

        int bufferStartPos = 0;

        int bufferCurrentPos = bufferStartPos;

        char type = '^';

        uint token = 0;

        while (bufferCurrentPos < streamOutput.Length)
        {
          if (((bufferCurrentPos + 1) >= streamOutput.Length) || (streamOutput [bufferCurrentPos + 1] == ','))
          {
            string clazz = recordData.Substring (bufferStartPos, (bufferCurrentPos + 1) - bufferStartPos);

            string data = string.Empty;

            if (((bufferCurrentPos + 1) < streamOutput.Length) && (streamOutput [bufferCurrentPos + 1] == ','))
            {
              data = recordData.Substring (bufferCurrentPos + 2);
            }

            MiRecord resultRecord = null;

            List<MiResultValue> values = new List<MiResultValue> ();

            ParseAllResults (data, ref values);

            switch (type)
            {
              case '^': resultRecord = new MiResultRecord (token, clazz, values); break;
              case '*': resultRecord = new MiAsyncRecord (MiAsyncRecord.AsyncType.Exec, token, clazz, values); break;
              case '+': resultRecord = new MiAsyncRecord (MiAsyncRecord.AsyncType.Status, token, clazz, values); break;
              case '=': resultRecord = new MiAsyncRecord (MiAsyncRecord.AsyncType.Notify, token, clazz, values); break;
            }

            return resultRecord;
          }
          else if ((recordData [bufferCurrentPos] == '^') || (recordData [bufferCurrentPos] == '*') || (recordData [bufferCurrentPos] == '+') || (recordData [bufferCurrentPos] == '='))
          {
            type = recordData [bufferCurrentPos];

            string stringToken = recordData.Substring (bufferStartPos, bufferCurrentPos);

            if (!string.IsNullOrWhiteSpace (stringToken))
            {
              token = uint.Parse (stringToken);
            }

            bufferStartPos = ++bufferCurrentPos;
          }

          ++bufferCurrentPos;
        }

        return null;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private static void ParseAllResults (string streamOutput, ref List<MiResultValue> results)
    {
      int bufferStartPos = 0;

      int bufferCurrentPos = bufferStartPos;

      int enclosureCount = 0;

      int enclosuresProcessed = 0;

      bool insideQuotationEnclosure = false;

      string enclosureVariable = string.Empty;

      while (bufferCurrentPos < streamOutput.Length)
      {
        if ((streamOutput [bufferCurrentPos] == '=') && (enclosureCount == 0))
        {
          enclosureVariable = streamOutput.Substring (bufferStartPos, bufferCurrentPos - bufferStartPos);

          bufferStartPos = ++bufferCurrentPos;
        }

        if ((streamOutput [bufferCurrentPos] == '[') || (streamOutput [bufferCurrentPos] == '{'))
        {
          ++enclosureCount;
        }
        else if ((streamOutput [bufferCurrentPos] == ']') || (streamOutput [bufferCurrentPos] == '}'))
        {
          --enclosureCount;
        }
        else if (streamOutput [bufferCurrentPos] == '\"')
        {
          if (insideQuotationEnclosure)
          {
            --enclosureCount;
          }
          else
          {
            ++enclosureCount;
          }

          insideQuotationEnclosure = !insideQuotationEnclosure;
        }

        if (((bufferCurrentPos + 1) >= streamOutput.Length) || ((streamOutput [bufferCurrentPos + 1] == ',') && (enclosureCount == 0)))
        {
          // 
          // Handle a nested enclosure, const-variable, or the end of a string.
          // 

          List<MiResultValue> nestedResultValues = new List<MiResultValue> ();

          if (enclosureVariable == string.Empty)
          {
            enclosureVariable = enclosuresProcessed.ToString ();
          }

          string enclosedSegment = streamOutput.Substring (bufferStartPos, (bufferCurrentPos + 1) - bufferStartPos);

          if (enclosedSegment.StartsWith ("["))
          {
            string listValue = enclosedSegment.Substring (1, enclosedSegment.Length - 2); // remove [] conatiner

            ParseAllResults (listValue, ref nestedResultValues);

            results.Add (new MiResultValueList (enclosureVariable, nestedResultValues));
          }
          else if (enclosedSegment.StartsWith ("{"))
          {
            string tupleValue = enclosedSegment.Substring (1, enclosedSegment.Length - 2); // remove {} conatiner

            ParseAllResults (tupleValue, ref nestedResultValues);

            results.Add (new MiResultValueTuple (enclosureVariable, nestedResultValues));
          }
          else
          {
            string constValue = enclosedSegment.Substring (1, enclosedSegment.Length - 2); // remove quotation-marks

            results.Add (new MiResultValueConst (enclosureVariable, constValue));
          }

          ++enclosuresProcessed;

          enclosureVariable = string.Empty;

          insideQuotationEnclosure = false;

          ++bufferCurrentPos;

          bufferStartPos = bufferCurrentPos + 1;
        }

        ++bufferCurrentPos;
      }
    }

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