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
    internal Vector3 vector = Vector3.zero;

    public ConsoleVec3( string str )
    {
        if( !str.StartsWith( "(" ) || !str.EndsWith( ")" ) || str.Count( c => c == ',' ) != 2 )
        {
            Console.Write( $"Malformed Vector3 '{str}'" );
            return;
        }

        string[] values = str.Trim( '(', ')' ).Filter( true, true, false ).Split( ',' );
        if( values.Length != 3 )
        {
            Console.Write( $"Malformed Vector3 '{str}'" );
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
}

#endregion


#region CONSOLE

public static class Console
{
    private static Dictionary<string, FieldInfo> consoleVars = new();
    private static Dictionary<string, MethodInfo> consoleFuncs = new();

    public delegate void TextWrittenDelegate( string text );

    public static TextWrittenDelegate OnTextWritten;

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
        }

        //TODO: Implement functions... How do I want to handle parameters? 
        // var funcs = mainAssembly.GetTypes()
        //     .SelectMany( t =>
        //         t.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static )
        //             .Where( m => m.HasAttribute<ConsoleFuncAttribute>() ) );
        //
        // foreach( var func in funcs )
        // {
        //     consoleFuncs.Add( func.Name, func );
        // }

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
                Write( conv.ConvertToString( var.GetValue( null ) ) );
            }
            else
            {
                string value = command.Substring( pos + 1 );

                try
                {
                    var.SetValueOptimized( null, conv.ConvertFromString( value ) );
                    Write( conv.ConvertToString( var.GetValue( null ) ) );
                }
                catch( Exception e )
                {
                    string eMsg = e.ToString();
                    Write( $"Error: '{eMsg.Substring( 0, eMsg.IndexOf( '\n' ) )}'" );
                }
            }
        }
        else
        {
            Write( "Unknown variable / command." );
        }
    }

    public static void Write( string text )
    {
        OnTextWritten?.Invoke( text );
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
}

public class DefaultConsoleUI : MonoBehaviour
{
    private const int CommandHistoryCount = 10;
    private bool enabled = false;
    private string input = string.Empty;
    private string output = string.Empty;
    private int historyIndex = -1;

    private List<string> commandHistory = new();

    [ConsoleVar] private static float ConsoleHeightPercentage = 0.2f;

    private void Awake( )
    {
        Console.OnTextWritten += OnTextWritten;
    }

    private void OnTextWritten( string text )
    {
        if( text == string.Empty )
        {
            return;
        }

        output += text + "\n";
    }

    public void Update( )
    {
        if( Input.GetKeyDown( KeyCode.BackQuote ) )
        {
            enabled = !enabled;
            return;
        }

        if( Input.GetKeyDown( KeyCode.Backspace ) && input.Length > 0 )
        {
            input = input.Substring( 0, input.Length - 1 );
        }

        if( Input.GetKeyDown( KeyCode.UpArrow ) && commandHistory.Count != 0 )
        {
            historyIndex = Mathf.Clamp( historyIndex + 1, 0, commandHistory.Count - 1 );
            input = commandHistory[historyIndex];
        }
        else if( Input.GetKeyDown( KeyCode.DownArrow ) && commandHistory.Count != 0 )
        {
            historyIndex = Mathf.Clamp( historyIndex - 1, -1, commandHistory.Count - 1 );
            if( historyIndex < 0 )
            {
                input = string.Empty;
            }
            else
            {
                input = commandHistory[historyIndex];
            }
        }

        input += Input.inputString.RemoveSpecialCharacters();

        if( Input.GetKeyDown( KeyCode.Return ) )
        {
            if( commandHistory.Count == CommandHistoryCount )
            {
                commandHistory.RemoveAt( 0 );
            }

            commandHistory.Add( input );

            output = input + "\n";
            Console.ExecuteCommand( input );
            input = string.Empty;
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

        Rect consoleRect = new Rect( 0, 0, Screen.width, Screen.height * ConsoleHeightPercentage );
        Rect inputRect = new Rect( 0, consoleRect.height, Screen.width, GUI.skin.textArea.lineHeight * 1.5f );

        GUI.Box( consoleRect, output, style );
        GUI.Box( inputRect, input, style );
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
}

#endregion