#define DEFAULT_UI_IN_USE

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;

#region ATTRIBUTES

public class ConsoleVarAttribute : Attribute
{
}

public class ConsoleFuncAttribute : Attribute
{
}

#endregion


#region CONSOLE_TYPES

public class ConsoleVec3Converter : TypeConverter
{
    public override object ConvertTo( ITypeDescriptorContext context, CultureInfo culture, object value,
        Type destinationType )
    {
        if( destinationType != typeof( string ) )
        {
            return null;
        }

        return ( ( ConsoleVec3 ) value ).vector.ToString();
    }

    public override bool CanConvertFrom( ITypeDescriptorContext context, Type sourceType )
    {
        return sourceType == typeof( string );
    }

    public override object ConvertFrom( ITypeDescriptorContext context, CultureInfo culture, object value )
    {
        return new ConsoleVec3( ( string ) value );
    }
}

[TypeConverter( typeof( ConsoleVec3Converter ) )]
public class ConsoleVec3
{
    internal Vector3 vector;

    public ConsoleVec3( string str )
    {
        if( str.Count( c => c == ',' ) != 2 )
        {
            Console.WriteResult( $" <color = red>Malformed Vector3 '{str}' </color>" );
            return;
        }

        string[] values = str.TrimStart( '(' ).TrimEnd( ')' ).Filter( true, true, false ).Split( ',' );
        if( values.Length != 3 )
        {
            Console.WriteResult( $"<color = red>Malformed Vector3 '{str}'</color>" );
            return;
        }

        vector = new Vector3( Convert.ToSingle( values[0] ), Convert.ToSingle( values[1] ),
            Convert.ToSingle( values[2] ) );
    }

    public ConsoleVec3( float x, float y, float z )
    {
        vector = new Vector3( x, y, z );
    }

    public static implicit operator Vector3( ConsoleVec3 vec3 )
    {
        return vec3.vector;
    }

    public override string ToString( )
    {
        return vector.ToString();
    }
}

#endregion


#region CONSOLE

public static class Console
{
    private static Dictionary<string, FieldInfo> consoleVars = new();
    private static Dictionary<string, MethodInfo> consoleFuncs = new();

    private static List<string> names = new();

    public delegate void TextWrittenDelegate( string text );

    public static TextWrittenDelegate OnTextWritten;
    public static TextWrittenDelegate OnResultWritten;

    [RuntimeInitializeOnLoadMethod]
    public static void Init( )
    {
        Assembly mainAssembly = Assembly.GetAssembly( typeof( Console ) );

        var vars = mainAssembly.GetTypes()
            .SelectMany( t =>
                t.GetFields( BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic )
                    .Where( m => m.HasAttribute<ConsoleVarAttribute>() ) );

        foreach( FieldInfo var in vars )
        {
            consoleVars.Add( var.Name, var );
            names.Add( var.Name );
        }

        // TODO: Implement functions... How do I want to handle parameters? 
        var funcs = mainAssembly.GetTypes()
            .SelectMany( t =>
                t.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static )
                    .Where( m => m.HasAttribute<ConsoleFuncAttribute>() ) );

        foreach( var func in funcs )
        {
            consoleFuncs.Add( func.Name, func );
            names.Add( func.Name );
        }

#if DEFAULT_UI_IN_USE
        GameObject uiObject = new GameObject( "DebugConsoleUI" );
        uiObject.AddComponent<DefaultConsoleUI>();
#endif
    }

    public static void ExecuteCommand( string command )
    {
        int pos = command.IndexOf( ' ' );
        if( pos < 0 )
        {
            pos = command.Length;
        }

        string target = command.Substring( 0, pos );

        if( consoleVars.TryGetValue( target, out var var ) )
        {
            TypeConverter conv = TypeDescriptor.GetConverter( var.FieldType );
            if( pos >= command.Length )
            {
                WriteResult( conv.ConvertToString( var.GetValue( null ) ) );
            }
            else
            {
                string value = command.Substring( pos + 1 );

                // try
                {
                    var.SetValueOptimized( null, conv.ConvertFromString( value ) );
                    WriteResult( conv.ConvertToString( var.GetValue( null ) ) );
                }
                // catch( Exception e )
                // {
                // string eMsg = e.ToString();
                // WriteResult( $"<color=red>Error: '{eMsg.Substring( 0, eMsg.IndexOf( '\n' ) )}'</color>" );
                // }
            }
        }
        else if( consoleFuncs.TryGetValue( target, out var func ) )
        {
            ParameterInfo[] paramInfos = func.GetParameters();

            if( paramInfos.Length > 0 && pos >= command.Length )
            {
                WriteResult( $"<color=red>Incorrect number of parameters, expected {paramInfos.Length}</color>" );
                return;
            }

            List<object> arguments = new List<object>();
            string argString = command.Substring( pos + 1 );
            string[] args = argString.SplitArguments();
            int argIndex = 0;

            for( ; argIndex < args.Length; argIndex++ )
            {
                var arg = args[argIndex];
                var param = paramInfos[argIndex];

                TypeConverter conv = TypeDescriptor.GetConverter( param.ParameterType );

                try
                {
                    arguments.Add( conv.ConvertFromString( arg ) );
                }
                catch( Exception e )
                {
                    string eMsg = e.ToString();
                    WriteResult( $"<color=red>Error: '{eMsg.Substring( 0, eMsg.IndexOf( '\n' ) )}'</color>" );
                    return;
                }
            }

            // Fill remaining optional parameters with default values so the func gets its default value for that param
            if( argIndex < paramInfos.Length )
            {
                for( ; argIndex < paramInfos.Length; argIndex++ )
                {
                    ParameterInfo param = paramInfos[argIndex];
                    if( !param.IsOptional )
                    {
                        break;
                    }

                    arguments.Add( param.DefaultValue );
                }
            }

            try
            {
                object retVal = func.InvokeOptimized( null, arguments.ToArray() );
                if( retVal != null )
                {
                    WriteResult( retVal.ToString() );
                }
            }
            catch( Exception e )
            {
                string eMsg = e.ToString();
                WriteResult( $"<color=red>Error: '{eMsg.Substring( 0, eMsg.IndexOf( '\n' ) )}'</color>" );
            }
        }
        else
        {
            WriteResult( "<color=red>Unknown variable / command.</color>" );
        }
    }

    public static void Write( string text )
    {
        OnTextWritten?.Invoke( text );
    }

    public static void WriteResult( string text )
    {
        OnResultWritten?.Invoke( text );
    }

    public static string[] RecommendedCompletions( string input )
    {
        return names.FuzzyFilter( input );
    }

    public static string GetDescription( string input )
    {
        //Cut out anything but the command / var itself (for now at least).
        var index = input.IndexOf( ' ' );
        if( index > 0 )
        {
            input = input.Substring( 0, index );
        }

        if( consoleVars.TryGetValue( input, out var var ) )
        {
            return $"{var.FieldType.Name} {var.Name} = {var.GetValue( null )}";
        }

        if( consoleFuncs.TryGetValue( input, out var func ) )
        {
            StringBuilder sb = new StringBuilder();
            sb.Append( $"{func.ReturnType.Name} {func.Name}( " );

            ParameterInfo[] paramInfos = func.GetParameters();
            for( var i = 0; i < paramInfos.Length; i++ )
            {
                var param = func.GetParameters()[i];
                sb.Append( $"{param.Name} : {param.ParameterType.Name}" );
                if( param.IsOptional )
                {
                    sb.Append( $" (={param.DefaultValue})" );
                }

                if( i < paramInfos.Length - 1 )
                {
                    sb.Append( ", " );
                }
            }

            sb.Append( " )" );
            return sb.ToString();
        }

        return string.Empty;
    }
}

#endregion

#if DEFAULT_UI_IN_USE

#region DEFAULT_UI

static class StringExtensions
{
    public static string RemoveSpecialCharacters( this string str )
    {
        StringBuilder sb = new StringBuilder();
        foreach( char c in str )
        {
            if( c is '\b' or '\r' )
            {
                continue;
            }

            sb.Append( c );
        }

        return sb.ToString();
    }

    public static string[] SplitArguments( this string str )
    {
        List<string> args = new();
        StringBuilder sb = new StringBuilder();

        bool parsingCustomType = false;

        foreach( char c in str )
        {
            if( c == '(' || c == '"' && !parsingCustomType )
            {
                parsingCustomType = true;
                continue;
            }

            if( c == ')' || c == '"' && parsingCustomType )
            {
                parsingCustomType = false;
                continue;
            }

            if( c == ' ' && !parsingCustomType )
            {
                args.Add( sb.ToString() );
                sb.Clear();
                continue;
            }

            sb.Append( c );
        }

        if( sb.Length > 0 )
        {
            args.Add( sb.ToString() );
        }

        return args.ToArray();
    }

    public static int FuzzyScore( this string str, string target )
    {
        if( !Fts.FuzzyMatch( target, str, out var score ) )
        {
            return -1;
        }

        return score;
    }

    //TODO: Make this not match twice per string.
    public static string[] FuzzyFilter( this IEnumerable<string> choices, string target )
    {
        return choices.Where( s => s.FuzzyScore( target ) >= 0 ).OrderByDescending( s => s.FuzzyScore( target ) )
            .ToArray();
    }
}

public class DefaultConsoleUI : MonoBehaviour
{
    private bool enabled = false;
    private string input = string.Empty;
    private int historyIndex = -1;
    private int recommendationIndex = -1;
    private List<string> output = new();
    private string result = string.Empty;

    private List<string> commandHistory = new();
    private string[] recommendations = Array.Empty<string>();

    private float lastDeleteTime = 0.0f;

    [ConsoleVar] private static int consoleRecommendationCount = 5;
    [ConsoleVar] private static int consoleCommandHistoryCount = 10;
    [ConsoleVar] private static int consoleOutputHistoryCount = 5;
    [ConsoleVar] private static float consoleDeleteInterval = 0.1f;

    private void Awake( )
    {
        Console.OnTextWritten += OnTextWritten;
        Console.OnResultWritten += OnResultWritten;
    }

    private void OnTextWritten( string text )
    {
        if( text == string.Empty )
        {
            return;
        }

        output.Add( text );
    }

    private void OnResultWritten( string text )
    {
        result += text;
    }

    public void Update( )
    {
        if( Input.GetKeyDown( KeyCode.BackQuote ) )
        {
            enabled = !enabled;
            return;
        }

        if( Input.GetKey( KeyCode.Backspace ) && input.Length > 0 )
        {
            if( Time.time - lastDeleteTime > consoleDeleteInterval )
            {
                input = input.Substring( 0, input.Length - 1 );
                lastDeleteTime = Time.time;
            }
        }

        if( Input.GetKeyDown( KeyCode.UpArrow ) && commandHistory.Count != 0 )
        {
            historyIndex = Mathf.Clamp( historyIndex + 1, 0, commandHistory.Count - 1 );
            input = commandHistory[historyIndex];
        }
        else if( Input.GetKeyDown( KeyCode.DownArrow ) && commandHistory.Count != 0 )
        {
            historyIndex = Mathf.Clamp( historyIndex - 1, -1, commandHistory.Count - 1 );
            input = historyIndex < 0 ? string.Empty : commandHistory[historyIndex];
        }

        //Only process func / var recommendations if we're entering the function
        if( input.IndexOf( ' ' ) < 0 )
        {
            recommendations = Console.RecommendedCompletions( input ).Take( consoleRecommendationCount ).ToArray();
            recommendationIndex = Mathf.Clamp( recommendationIndex, -1, recommendations.Length );
            if( Input.GetKeyDown( KeyCode.Tab ) )
            {
                recommendationIndex = ( recommendationIndex + 1 ) % recommendations.Length;
            }

            if( Input.GetKeyDown( KeyCode.Space ) && recommendationIndex >= 0 )
            {
                input = recommendations[recommendationIndex];
            }
        }
        else
        {
            //Otherwise just print info about the thing
            recommendations = new[] { Console.GetDescription( input ) };
        }


        string newInput = Input.inputString.RemoveSpecialCharacters();
        if( newInput.Length > 0 )
        {
            recommendationIndex = -1;
        }

        input += newInput;

        if( Input.GetKeyDown( KeyCode.Return ) )
        {
            if( commandHistory.Count == consoleCommandHistoryCount )
            {
                commandHistory.RemoveAt( 0 );
            }

            if( output.Count == consoleOutputHistoryCount )
            {
                output.RemoveAt( 0 );
            }

            string command = recommendationIndex >= 0 ? recommendations[recommendationIndex] : input;
            commandHistory.Insert( 0, command );

            Console.ExecuteCommand( command );

            output.Add( result.Length > 0 ? $"{command} -> {result}" : command );

            result = string.Empty;
            input = string.Empty;
            recommendationIndex = -1;
            historyIndex = -1;
        }
    }

    public void OnGUI( )
    {
        if( !enabled )
        {
            return;
        }

        GUIStyle style = new GUIStyle( GUI.skin.box );
        style.alignment = TextAnchor.UpperLeft;

        float lineHeight = GUI.skin.box.lineHeight;
        Rect consoleRect = new Rect( 0, 0, Screen.width, lineHeight * ( consoleOutputHistoryCount + 0.5f ) );
        Rect inputRect = new Rect( 0, consoleRect.height, Screen.width, lineHeight * 1.5f );
        Rect completionRect = new Rect( 0, inputRect.y + inputRect.height, Screen.width,
            lineHeight * ( recommendations.Length + 0.5f ) );

        GUI.Box( consoleRect, output.ToLineSeparatedString(), style );
        GUI.Box( inputRect, input, style );
        if( recommendations.Length > 0 )
        {
            StringBuilder sb = new StringBuilder();
            for( var index = 0; index < recommendations.Length; index++ )
            {
                var recommendation = recommendations[index];
                sb.AppendLine( recommendationIndex == index ? $"<b><i>{recommendation}</i></b>" : recommendation );
            }

            GUI.Box( completionRect, sb.ToString(), style );
        }
    }
}

#endregion

#endif

#region Tests

public class MyTestClass
{
    public enum TestEnum
    {
        EnumA,
        EnumB,
        EnumC
    }

    [ConsoleVar] private static string testString = "Test";
    [ConsoleVar] private static int testInt = -1;
    [ConsoleVar] private static bool testBool = false;
    [ConsoleVar] private static float testFloat = -1.0f;
    [ConsoleVar] private static TestEnum testEnum = TestEnum.EnumA;
    [ConsoleVar] private static ConsoleVec3 testVec = new( 0, 0, 0 );

    [ConsoleFunc]
    private static int AddInt( int a, int b )
    {
        return a + b;
    }

    [ConsoleFunc]
    private static float AddFloat( float a, float b )
    {
        return a + b;
    }

    [ConsoleFunc]
    private static void LogString( string text )
    {
        Debug.Log( text );
    }

    [ConsoleFunc]
    private static Vector3 ScaleVec( ConsoleVec3 vec, float scale )
    {
        return ( Vector3 ) vec * scale;
    }

    [ConsoleFunc]
    private static void RepeatString( string str, int numRepeats )
    {
        for( int i = 0; i < numRepeats; i++ )
        {
            Console.WriteResult( str + ", " );
        }
    }

    [ConsoleFunc]
    private static void OptionalArgTest( string req, int numRepeats = 2 )
    {
        RepeatString( req, numRepeats );
    }
}

#endregion

#region FTS_FUZZY_SEARCH

public class Fts
{
    public static bool FuzzyMatchSimple( string pattern, string str )
    {
        var patternIdx = 0;
        var strIdx = 0;
        var patternLength = pattern.Length;
        var strLength = str.Length;

        while( patternIdx != patternLength && strIdx != strLength )
        {
            var patternChar = char.ToLowerInvariant( pattern[patternIdx] );
            var strChar = char.ToLowerInvariant( str[strIdx] );
            if( patternChar == strChar ) ++patternIdx;
            ++strIdx;
        }

        return patternLength != 0 && strLength != 0 && patternIdx == patternLength;
    }

    public static bool FuzzyMatch( string pattern, string str, out int outScore )
    {
        byte[] matches = new byte[256];
        return FuzzyMatch( pattern, str, out outScore, matches, matches.Length );
    }

    public static bool FuzzyMatch( string pattern, string str, out int outScore, byte[] matches, int maxMatches )
    {
        int recursionCount = 0;
        int recursionLimit = 10;

        return FuzzyMatchRecursive( pattern, str, 0, 0, out outScore, null, matches, maxMatches, 0,
            ref recursionCount, recursionLimit );
    }

    // Private implementation
    static bool FuzzyMatchRecursive( string pattern, string str,
        int patternCurIndex,
        int strCurrIndex, out int outScore,
        byte[] srcMatches, byte[] matches, int maxMatches,
        int nextMatch, ref int recursionCount, int recursionLimit )
    {
        outScore = 0;
        // Count recursions
        ++recursionCount;
        if( recursionCount >= recursionLimit )
            return false;

        // Detect end of strings
        if( patternCurIndex == pattern.Length || strCurrIndex == str.Length )
            return false;

        // Recursion params
        bool recursiveMatch = false;
        byte[] bestRecursiveMatches = new byte[256];
        int bestRecursiveScore = 0;

        // Loop through pattern and str looking for a match
        bool firstMatch = true;
        while( patternCurIndex < pattern.Length && strCurrIndex < str.Length )
        {
            // Found match
            if( Char.ToLowerInvariant( pattern[patternCurIndex] ) == Char.ToLowerInvariant( str[strCurrIndex] ) )
            {
                // Supplied matches buffer was too short
                if( nextMatch >= maxMatches )
                    return false;

                // "Copy-on-Write" srcMatches into matches
                if( firstMatch && srcMatches != null )
                {
                    Buffer.BlockCopy( srcMatches, 0, matches, 0, nextMatch );
                    // memcpy(matches, srcMatches, nextMatch);
                    firstMatch = false;
                }

                // Recursive call that "skips" this match
                byte[] recursiveMatches = new byte[256];
                if( FuzzyMatchRecursive( pattern, str, patternCurIndex, strCurrIndex + 1, out int recursiveScore,
                       matches, recursiveMatches, recursiveMatches.Length, nextMatch, ref recursionCount,
                       recursionLimit ) )
                {
                    // Pick best recursive score
                    if( !recursiveMatch || recursiveScore > bestRecursiveScore )
                    {
                        Buffer.BlockCopy( recursiveMatches, 0, bestRecursiveMatches, 0, 256 );
                        // memcpy(bestRecursiveMatches, recursiveMatches, 256);
                        bestRecursiveScore = recursiveScore;
                    }

                    recursiveMatch = true;
                }

                // Advance
                matches[nextMatch++] = ( byte ) strCurrIndex;
                ++patternCurIndex;
            }

            ++strCurrIndex;
        }

        // Determine if full pattern was matched
        bool matched = patternCurIndex == pattern.Length;

        // Calculate score
        if( matched )
        {
            const int sequentialBonus = 15; // bonus for adjacent matches
            const int separatorBonus = 30; // bonus if match occurs after a separator
            const int camelBonus = 30; // bonus if match is uppercase and prev is lower
            const int firstLetterBonus = 15; // bonus if the first letter is matched

            const int leadingLetterPenalty = -5; // penalty applied for every letter in str before the first match
            const int maxLeadingLetterPenalty = -15; // maximum penalty for leading letters
            const int unmatchedLetterPenalty = -1; // penalty for every letter that doesn't matter

            // Iterate str to end
            strCurrIndex = str.Length;

            // Initialize score
            outScore = 100;

            // Apply leading letter penalty
            int penalty = leadingLetterPenalty * matches[0];
            if( penalty < maxLeadingLetterPenalty )
                penalty = maxLeadingLetterPenalty;
            outScore += penalty;
            // Apply unmatched penalty
            int unmatched = strCurrIndex - nextMatch;
            outScore += unmatchedLetterPenalty * unmatched;
            // Apply ordering bonuses
            for( int i = 0; i < nextMatch; ++i )
            {
                byte currIdx = matches[i];

                if( i > 0 )
                {
                    byte prevIdx = matches[i - 1];

                    // Sequential
                    if( currIdx == ( prevIdx + 1 ) )
                        outScore += sequentialBonus;
                }

                // Check for bonuses based on neighbor character value
                if( currIdx > 0 )
                {
                    // Camel case
                    char neighbor = str[currIdx - 1];
                    char curr = str[currIdx];
                    // the js impl does neighbor === neighbor.toLowerCase() && curr === curr.toUpperCase()
                    // as lowering/upping a digit returns the same digit, it counts as a camel bonus
                    if( Char.IsLower( neighbor ) && Char.IsUpper( curr ) )
                        outScore += camelBonus;

                    // Separator
                    bool neighborSeparator = neighbor == '_' || neighbor == ' ' || char.IsDigit( neighbor );
                    if( neighborSeparator )
                        outScore += separatorBonus;
                }
                else
                {
                    // First letter
                    outScore += firstLetterBonus;
                }
            }
        }

        // Return best result
        if( recursiveMatch && ( !matched || bestRecursiveScore > outScore ) )
        {
            // Recursive score is better than "this"
            Buffer.BlockCopy( bestRecursiveMatches, 0, matches, 0, maxMatches );
            // memcpy(matches, bestRecursiveMatches, maxMatches);
            outScore = bestRecursiveScore;
            return true;
        }
        else if( matched )
        {
            // "this" score is better than recursive
            return true;
        }
        else
        {
            // no match
            return false;
        }
    }
}

#endregion