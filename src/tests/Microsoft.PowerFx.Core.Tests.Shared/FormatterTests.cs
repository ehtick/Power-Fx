﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Core.Logging;
using Microsoft.PowerFx.Core.Tests;
using Microsoft.PowerFx.Core.Texl.Builtins;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;
using Xunit;
using static Microsoft.PowerFx.Core.Parser.TexlParser;

namespace Microsoft.PowerFx.Tests
{
    public sealed class FormatterTests : PowerFxTest
    {
        [Theory]
        [InlineData(
            "Collect(Yep, { a: [1], b: \"Hello\" })",
            "Collect(#$firstname$#, { #$fieldname$#:[ #$decimal$# ], #$fieldname$#:#$string$# })")]
        [InlineData(
            "Set(x, 10 + 3); Launch(\"example.com\", ThisItem.Text, Parent.Text)",
            "Set(#$firstname$#, #$decimal$# + #$decimal$#) ; Launch(#$string$#, #$firstname$#.#$righthandid$#, Parent.#$righthandid$#)")]
        [InlineData(
            "$\"Hello {\"World\"}\"",
            "$\"#$string$##$string$#\"")]
        [InlineData(
            "$\"Hello {5}\"",
            "$\"#$string$#{#$decimal$#}\"")]
        [InlineData(
            "ParseJSON(\"[{ \"\"Age\"\": 5}]\", Type([{Age: Number}]))",
            "ParseJSON(#$string$#, Type([ { #$fieldname$#:#$firstname$# } ]))")]
        [InlineData(
            "ParseJSON(\"{}\", Type(RecordOf(Accounts)))",
            "ParseJSON(#$string$#, Type(RecordOf(#$firstname$#)))")]
        public void TestStucturalPrint(string script, string expected)
        {
            var result = ParseScript(
                script,
                flags: Flags.EnableExpressionChaining,
                features: Features.PowerFxV1);

            var actual = StructuralPrint.Print(result.Root);
            Assert.Equal(expected, actual);

            // Test same cases via CheckResult
            var check = new CheckResult(new Engine(new PowerFxConfig()));
            check.SetText(script, new ParserOptions { AllowsSideEffects = true });
            var result2 = check.ApplyGetLogging();
            Assert.Equal(expected, result2);
        }

        [Theory]
        [InlineData(
            "With({t:Table({a:1},{a:2})},t)",
            "With({ #$fieldname$#:Table({ #$fieldname$#:#$decimal$# }, { #$fieldname$#:#$decimal$# }) }, #$firstname$#)",
            "With({ #$fieldname$#:Table({ #$fieldname$#:#$decimal$# }, { #$fieldname$#:#$decimal$# }) }, #$LambdaField$#)")]
        [InlineData(
            "Set(x, 1); Set(y, 2); x + y",
            "Set(#$firstname$#, #$decimal$#) ; Set(#$firstname$#, #$decimal$#) ; #$firstname$# + #$firstname$#",
            "Set(#$firstname$#, #$decimal$#) ; Set(#$firstname$#, #$decimal$#) ; #$firstname$# + #$firstname$#")]
        [InlineData(
            "ForAll([1,2,3], Value * 2)",
            "ForAll([ #$decimal$#, #$decimal$#, #$decimal$# ], #$firstname$# * #$decimal$#)",
            "ForAll([ #$decimal$#, #$decimal$#, #$decimal$# ], #$LambdaField$# * #$decimal$#)")]
        [InlineData(
            "ForAll([1,2,3], ThisRecord.Value * 2)",
            "ForAll([ #$decimal$#, #$decimal$#, #$decimal$# ], #$firstname$#.#$righthandid$# * #$decimal$#)",
            "ForAll([ #$decimal$#, #$decimal$#, #$decimal$# ], #$LambdaFullRecord$#.#$righthandid$# * #$decimal$#)")]
        [InlineData(
            "Join(Table({a:1}), Table({a:1}), LeftRecord.a = RightRecord.a, JoinType.Inner, RightRecord.a As AAA)",
            "Join(Table({ #$fieldname$#:#$decimal$# }), Table({ #$fieldname$#:#$decimal$# }), #$firstname$#.#$righthandid$# = #$firstname$#.#$righthandid$#, #$firstname$#.#$righthandid$#, #$firstname$#.#$righthandid$# As #$righthandid$#)",
            "Join(Table({ #$fieldname$#:#$decimal$# }), Table({ #$fieldname$#:#$decimal$# }), #$LambdaField$#.#$righthandid$# = #$LambdaField$#.#$righthandid$#, JoinType.Inner, #$LambdaField$#.#$righthandid$# As #$righthandid$#)")]
        [InlineData(
            "WeekNum(Date(2020, 12, 8),StartOfWeek.Sunday)",
            "WeekNum(Date(#$decimal$#, #$decimal$#, #$decimal$#), #$firstname$#.#$righthandid$#)",
            "WeekNum(Date(#$decimal$#, #$decimal$#, #$decimal$#), StartOfWeek.Sunday)")]

        public void TestStucturalPrintWithBinding(string script, string beforebinding, string afterbinding)
        {
            var result = ParseScript(
                script,
                flags: Flags.EnableExpressionChaining);

            var actual = StructuralPrint.Print(result.Root);
            Assert.Equal(beforebinding, actual);

            var symbolTable = new SymbolTable();
            symbolTable.AddFunction(new JoinFunction());

            // Test same cases via CheckResult
            var check = new CheckResult(new Engine());

            check.SetText(script, new ParserOptions { AllowsSideEffects = true })
                .SetBindingInfo(symbolTable)
                .ApplyBinding();

            actual = check.ApplyGetLogging();
            Assert.Equal(afterbinding, actual);
        }

        private class TestSanitizer : ISanitizedNameProvider
        {
            public bool TrySanitizeIdentifier(Identifier identifier, out string sanitizedName, DottedNameNode dottedNameNode = null)
            {
                sanitizedName = dottedNameNode == null ? "custom" : "custom2";
                return true;
            }
        }

        [Theory]
        [InlineData(
            "Function(Field,1,\"foo\")",
            "#$function$#(custom, #$decimal$#, #$string$#)")]
        [InlineData(
            "Lookup.Field && true",
            "custom.custom2 && #$boolean$#")]
        public void TestStucturalPrintWithCustomSanitizer(string script, string expected)
        {
            var result = ParseScript(
                script,
                flags: Flags.EnableExpressionChaining);

            Assert.Equal(expected, StructuralPrint.Print(result.Root, nameProvider: new TestSanitizer()));
        }

        [Theory]
        [InlineData("Back()", false)]
        [InlineData("false", false)]
        [InlineData("\"Are you sure you want to delete this \r\nreceipt?\"", false)]
        [InlineData("RGBA(\n    255,\n    255,\n    255,\n    1\n)", false)]
        [InlineData("RGBA(\n    255,\n    /*r   */255,   ", true)]
        public void TestSeverityLevelsForPrettyPrint(string script, bool expected)
        {
            var result = ParseScript(
                script,
                flags: Flags.EnableExpressionChaining);

            // Can't pretty print a script with errors.
            var hasErrorsWithSeverityHigherThanWarning = false;

            if (result.Errors != null && result.Errors.Any(x => x.Severity > ErrorSeverity.Warning))
            {
                hasErrorsWithSeverityHigherThanWarning = true;
            }

            Assert.Equal(hasErrorsWithSeverityHigherThanWarning, expected);
        }

        [Theory]
        [InlineData("Back()", "Back()")]
        [InlineData("false", "false")]
        [InlineData("\"Are you sure you want to delete this \r\nreceipt?\"", "\"Are you sure you want to delete this \r\nreceipt?\"")]
        [InlineData("RGBA(\n    255,\n    255,\n    255,\n    1\n)", "RGBA(255,255,255,1)")]
        [InlineData("RGBA(\n    255,\n    /*r   */255,\n    255,\n    1\n)//com   ", "RGBA(255,\n    /*r   */255,255,1)//com   ")]
        [InlineData("If(\n    Text(\n        Coalesce(\n            Sum(\n                Filter(\n                    Expenses,\n                    BudgetTitle = Gallery1.Selected.BudgetTitle && BudgetId=Gallery1.Selected.BudgetId\n                ),\n                Value(Expense)\n            ),\n            0\n        ),\n        \"$#,##\"\n    )=\"$\",\n    \"$0\",\n    Text(\n        Coalesce(\n            Sum(\n                Filter(\n                    Expenses,\n                    BudgetId = Gallery1.Selected.BudgetId\n                ),\n                Value(Expense)\n            ),\n            0\n        ),\n        \"$#,##\"\n    )\n)", "If(Text(Coalesce(Sum(Filter(Expenses,BudgetTitle=Gallery1.Selected.BudgetTitle&&BudgetId=Gallery1.Selected.BudgetId),Value(Expense)),0),\"$#,##\")=\"$\",\"$0\",Text(Coalesce(Sum(Filter(Expenses,BudgetId=Gallery1.Selected.BudgetId),Value(Expense)),0),\"$#,##\"))")]
        [InlineData("If(\n    Text(\n        Value(ThisItem.Expense)\n    )= \"0\",\n    \"$\",\n    Text(\n        Value(ThisItem.Expense),\n        \"$#,##\"\n    )\n)", "If(Text(Value(ThisItem.Expense))=\"0\",\"$\",Text(Value(ThisItem.Expense),\"$#,##\"))")]
        [InlineData("$\"1 + 2 is\n\n{6} not 3\"", "$\"1 + 2 is\n\n{6} not 3\"")]
        [InlineData("$\"\n\n1 + 2 is\n\n{6} not 3\n\n\t\"", "$\"\n\n1 + 2 is\n\n{6} not 3\n\n\t\"")]
        [InlineData("$\" Foo\n\n\n\n\n Bar\"\"{{1}}\"", "$\" Foo\n\n\n\n\n Bar\"\"{{1}}\"")]
        [InlineData("$\"{\"ddddd\"} Foo is bar\"", "$\"{\"ddddd\"} Foo is bar\"")]
        [InlineData("$\"        Foo is bar           \n\"", "$\"        Foo is bar           \n\"")]
        [InlineData("$\" String n{$\" Foo\"\"\n bar {4+4} rr\"} between {223} trail\"", "$\" String n{$\" Foo\"\"\n bar {4+4} rr\"} between {223} trail\"")]
        [InlineData("\" Foo bar space\" ; 34", "\" Foo bar space\";34")]
        public void TestRemoveWhiteSpace(string script, string expected)
        {
            var result = TexlLexer.InvariantLexer.RemoveWhiteSpace(script);
            Assert.NotNull(result);
            Assert.Equal(result, expected);
        }

        [Theory]
        [InlineData("//This is a singleline comment", "", true)]
        [InlineData("//This is a singleline comment", "//This is a singleline comment", false)]
        [InlineData("If(/*checks if 1 < 2*/1 < 2,/*computes\nthe sqrt of -1*/sqrt(-1), /* returns 0*/0) // if expression", "If(1<2,sqrt(-1),0)", true)]
        [InlineData("If(/*checks if 1 < 2*/1 < 2,/*computes\nthe sqrt of -1*/sqrt(-1), /* returns 0*/0) // if expression", "If(/*checks if 1 < 2*/1<2,/*computes\nthe sqrt of -1*/sqrt(-1),/* returns 0*/0)// if expression", false)]
        [InlineData("\n// single line comment\n2", "2", true)]
        [InlineData("\n// single line comment\n2", "\n// single line comment\n2", false)]
        [InlineData("1+/*sssss*/  /*ssss*/2", "1+2", true)]
        [InlineData("1+/*sssss*/  /*ssss*/2", "1+/*sssss*/  /*ssss*/2", false)]
        [InlineData("// line one\n// line two\n// line three\n", "", true)]
        [InlineData("false /*this is false*/true", " false  true ", true)]
        [InlineData("false /*this is false*/true", " false /*this is false*/ true ", false)]
        public void TestMinificationWithCommentRemovalInNonTextFirst(string script, string expected, bool removeComments = false)
        {
            // Arrange
            var flags = TexlLexer.Flags.None;
            var options = new MinificationOptions(flags, removeComments);

            // Act
            var result = TexlLexer.InvariantLexer.GetMinifiedScript(script, options);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("=//This is a singleline comment", "", true)]
        [InlineData("=//This is a singleline comment", "//This is a singleline comment", false)]
        [InlineData("${/*This is a singleline comment*/}", "${}", true)]
        [InlineData("${/*This is a singleline comment*/}", "${/*This is a singleline comment*/}", false)]
        [InlineData("${If(/*checks if 1 < 2*/1 < 2,/*computes\nthe sqrt of -1*/sqrt(-1), /* returns 0*/0) }", "${If(1<2,sqrt(-1),0)}", true)]
        [InlineData("${If(/*checks if 1 < 2*/1 < 2,/*computes\nthe sqrt of -1*/sqrt(-1), /* returns 0*/0) }", "${If(/*checks if 1 < 2*/1<2,/*computes\nthe sqrt of -1*/sqrt(-1),/* returns 0*/0)}", false)]
        [InlineData("${1+/*sssss*/  /*ssss*/2}", "${1+2}", true)]
        [InlineData("${1+/*sssss*/  /*ssss*/2}", "${1+/*sssss*/  /*ssss*/2}", false)]
        [InlineData("${false /*this is false*/true }", "${ false  true }", true)]
        [InlineData("${false /*this is false*/true}", "${ false /*this is false*/ true }", false)]
        [InlineData("${// this is single line comment\n1 + 1}", "${1+1}", true)]
        public void TestMinificationWithCommentRemovalInTextFirst(string script, string expected, bool removeComments = false)
        {
            // Arrange
            var flags = TexlLexer.Flags.TextFirst;
            var options = new MinificationOptions(flags, removeComments);

            // Act
            var result = TexlLexer.InvariantLexer.GetMinifiedScript(script, options);

            // Assert
            Assert.Equal(expected, result);
        }

        // Note going forward.  Ok, so here's the issue with this test.  there is currently a changable variable
        // place designed to alter minimun characters needed for a break to be made.  This was designed for future user ability
        // to change the char min limit.  Currently it is set at 50 chars.  Originally it was set at 0.
        // point being, everytime this is changed, it will change the "expected" text below and will break this test.
        [Theory]
        [InlineData("foo[@bar]", "foo[@bar]")]
        [InlineData("Back()", "Back()")]
        [InlineData("false", "false")]
        [InlineData("\"Are you sure you want to delete this \r\nreceipt?\"", "\"Are you sure you want to delete this \r\nreceipt?\"")]
        [InlineData("RGBA(255,255,255,1)", "RGBA(\n    255,\n    255,\n    255,\n    1\n)")]
        [InlineData("RGBA(255, /*r*/255, 255, 1)//com", "RGBA(\n    255,/*r*/\n    255,\n    255,\n    1\n)//com")]
        [InlineData("ColorFade(Button4.BorderColor, 20%)", "ColorFade(\n    Button4.BorderColor,\n    20%\n)")]
        [InlineData("If(!IsBlank(NewAddressText.Text)&&!IsBlank(NewCityText.Text)&&!IsBlank(NewZipText.Text)&&!IsBlank(NewStateText.Text)&&!IsBlank(NewTitleText.Text)&&!IsBlank(NewSubTitleText.Text)||!IsBlank(NewTitleText.Text)&&!IsBlank(NewSubTitleText.Text)&&Radio1_4.Selected.Value=\"Use GPS for current Location\", true)", "If(\n    !IsBlank(NewAddressText.Text) && !IsBlank(NewCityText.Text) && !IsBlank(NewZipText.Text) && !IsBlank(NewStateText.Text) && !IsBlank(NewTitleText.Text) && !IsBlank(NewSubTitleText.Text) || !IsBlank(NewTitleText.Text) && !IsBlank(NewSubTitleText.Text) && Radio1_4.Selected.Value = \"Use GPS for current Location\",\n    true\n)")]
        [InlineData("Set(ErrorC,0);ForAll(myData,Patch('[dbo].[BookingData]',First(Filter('[dbo].[BookingData]',ID=myData[@ID])),{FiscalYearValue:myData[@FiscalYearValue],ModifiedBy:TextInput1.Text}));If(CountRows(Errors('[dbo].[BookingData]'))>ErrorC,Set(Err,CountRows(Errors('[dbo].[BookingData]'))&\" \"&First(Errors('[dbo].[BookingData]')).Message),Set(Err,\"Success\"))", "Set(\n    ErrorC,\n    0\n);\nForAll(\n    myData,\n    Patch(\n        '[dbo].[BookingData]',\n        First(\n            Filter(\n                '[dbo].[BookingData]',\n                ID = myData[@ID]\n            )\n        ),\n        {\n            FiscalYearValue: myData[@FiscalYearValue],\n            ModifiedBy: TextInput1.Text\n        }\n    )\n);\nIf(\n    CountRows(Errors('[dbo].[BookingData]')) > ErrorC,\n    Set(\n        Err,\n        CountRows(Errors('[dbo].[BookingData]')) & \" \" & First(Errors('[dbo].[BookingData]')).Message\n    ),\n    Set(\n        Err,\n        \"Success\"\n    )\n)")]
        [InlineData("\"(\"&RoundUp(Value(UsedHrsText.Text/8),1) &\" DAYS)\"", "\"(\" & RoundUp(\n    Value(UsedHrsText.Text / 8),\n    1\n) & \" DAYS)\"")]
        [InlineData("If(true = !true,false,true)", "If(\n    true = !true,\n    false,\n    true\n)")]
        [InlineData("If(true > -1,false,true)", "If(\n    true > -1,\n    false,\n    true\n)")]
        [InlineData("If(1 <= -1,-11 >= -1,-11 < 1)", "If(\n    1 <= -1,\n    -11 >= -1,\n    -11 < 1\n)")]
        [InlineData("If(true <> -1,false,true)", "If(\n    true <> -1,\n    false,\n    true\n)")]
        [InlineData("If(true = Not true,false,true)", "If(\n    true = Not true,\n    false,\n    true\n)")]
        [InlineData("If(7% = !true,false,true)", "If(\n    7% = !true,\n    false,\n    true\n)")]
        [InlineData("-Label1.X & -Label1.Y", "-Label1.X & -Label1.Y")]
        [InlineData("(-Label1.X) & (-Label1.Y)", "(-Label1.X) & (-Label1.Y)")]
        [InlineData("UsedHrsText.Text-TextBox7_1.Text", "UsedHrsText.Text - TextBox7_1.Text")]
        [InlineData("If(!IsBlank(Address.Text) || !IsBlank(City.Text) || !IsBlank(States.Text) || !IsBlank(ZipCode.Text),SubmitForm(Form1),UpdateContext({error1:true}))", "If(\n    !IsBlank(Address.Text) || !IsBlank(City.Text) || !IsBlank(States.Text) || !IsBlank(ZipCode.Text),\n    SubmitForm(Form1),\n    UpdateContext({error1: true})\n)")]
        [InlineData("If(!IsBlank(Address.Text) && !IsBlank(City.Text) && !IsBlank(States.Text) && !IsBlank(ZipCode.Text),SubmitForm(Form1),UpdateContext({error1:true}))", "If(\n    !IsBlank(Address.Text) && !IsBlank(City.Text) && !IsBlank(States.Text) && !IsBlank(ZipCode.Text),\n    SubmitForm(Form1),\n    UpdateContext({error1: true})\n)")]
        [InlineData("If(CountRows(Filter('Time Off Requests','Created On'>=Today()&&Owner=LookUp(Users_1,'User Name'=User().Email,User)))>0,Navigate([@'Attendance Already Submitted'],ScreenTransition.Cover),NewForm(AttendanceForm));Navigate('Save Attendance',Fade)", "If(\n    CountRows(\n        Filter(\n            'Time Off Requests',\n            'Created On' >= Today() && Owner = LookUp(\n                Users_1,\n                'User Name' = User().Email,\n                User\n            )\n        )\n    ) > 0,\n    Navigate(\n        [@'Attendance Already Submitted'],\n        ScreenTransition.Cover\n    ),\n    NewForm(AttendanceForm)\n);\nNavigate(\n    'Save Attendance',\n    Fade\n)")]
        [InlineData("[1]", "[1]")]
        [InlineData("[1, 2, 3]", "[\n    1,\n    2,\n    3\n]")]
        [InlineData("If(true, [1, 2, 3], [3])", "If(\n    true,\n    [\n        1,\n        2,\n        3\n    ],\n    [3]\n)")]
        [InlineData("((((1 + 2))))", "((((1 + 2))))")]
        [InlineData("((1 + 2) + 3)", "((1 + 2) + 3)")]
        [InlineData("(1 + (2 + 3))", "(1 + (2 + 3))")]
        [InlineData("(1 * 2) + 3)", "(1 * 2) + 3)")]
        [InlineData("Namespace.Call(1, 2, 3)", "Namespace.Call(\n    1,\n    2,\n    3\n)")]
        [InlineData("ColorFade(RGBA(56,96,178,1),-(1+3)%%)", "ColorFade(\n    RGBA(\n        56,\n        96,\n        178,\n        1\n    ),\n    -(1 + 3)%%\n)")]
        [InlineData("/*jj*/\r\nRGBA(255, 255, 255, 1)\n//yes", "/*jj*/\r\nRGBA(\n    255,\n    255,\n    255,\n    1\n)\n//yes")]
        [InlineData("/*jj*/\nRGBA(\n    /*j2*/\n    255,\n    255,\n    255,\n    1\n)\n//yes", "/*jj*/\nRGBA(\n    /*j2*/\n    255,\n    255,\n    255,\n    1\n)\n//yes")]
        [InlineData("/*x*/Call(/*a*/1/*b*/;/*c*/2/*d*/;/*e*/3/*f*/, /*g*/4/*h*/)/*y*/", "/*x*/Call(/*a*/\n    1/*b*/;\n    /*c*/2/*d*/;\n    /*e*/3/*f*/,/*g*/\n    4/*h*/\n)/*y*/")]
        [InlineData("/*a*/[/*b*/1/*c*/,/*d*/2/*e*/]/*f*/", "/*a*/[\n    /*b*/1/*c*/,\n    /*d*/2/*e*/\n]/*f*/")]
        [InlineData("/*a*/{/*b*/name/*c*/:/*d*/1/*e*/,\n/*f*/name2/*g*/:/*h*/2/*i*/}/*j*/", "/*a*/{\n    /*b*/\n    name/*c*/: /*d*/1/*e*/,\n    /*f*/\n    name2/*g*/: /*h*/2/*i*/\n}/*j*/")]
        //// Make sure there's no lost trivia
        [InlineData("/*a*/foo/*b*/[/*c*/@/*d*/bar/*e*/]/*f*/", "/*a*/foo/*b*/[/*c*/@/*d*/bar/*e*/]/*f*/")]
        [InlineData("1; /*a*/2/*b*/; 3", "1;\n/*a*/2/*b*/;\n3")]
        [InlineData("/*a*/1/*b*/+/*c*/2/*d*/-/*e*/3/*f*/", "/*a*/1 /*b*/+/*c*/ 2 /*d*/-/*e*/ 3/*f*/")]
        [InlineData("$\"Hello {\"World\"}\"", "$\"Hello {\"World\"}\"")]
        [InlineData("$\"Hello { \"World\" }\"", "$\"Hello {\"World\"}\"")]
        [InlineData("$\"Hello {/*a*/\"World\"}\"", "$\"Hello {/*a*/\"World\"}\"")]
        [InlineData("/*a*/$\"Hello {\"World\"}\"", "/*a*/$\"Hello {\"World\"}\"")]
        [InlineData("$\"Hello {\"World\"/*b*/}\"", "$\"Hello {\"World\"/*b*/}\"")]
        [InlineData("$\"Hello {\"World\"}\"/*b*/", "$\"Hello {\"World\"}\"/*b*/")]
        [InlineData("$\"{{}}\"", "$\"{{}}\"")]
        [InlineData("This is not an interpolated {} {{{}}} string", "This is not an interpolated {} {{{}}} string")]
        [InlineData("$\"{{{{1+1}}}}\"", "$\"{{{{1+1}}}}\"")]
        [InlineData("Set(str, $\"{{}}\")", "Set(\n    str,\n    $\"{{}}\"\n)")]
        [InlineData("Set(additionText, $\"The sum of 1 and 3 is {{{1 + 3}}})\")", "Set(\n    additionText,\n    $\"The sum of 1 and 3 is {{{1 + 3}}})\"\n)")]
        [InlineData("$\"This is {{\"Another\"}} interpolated {{string}}\"", "$\"This is {{\"Another\"}} interpolated {{string}}\"")]
        [InlineData("ParseJSON(\"[]\", Type([{Age: Number}]))", "ParseJSON(\n    \"[]\",\n    Type([{Age: Number}])\n)")]
        [InlineData("Type([{Age: Number, Name: Text}])", "Type(\n    [\n        {\n            Age: Number,\n            Name: Text\n        }\n    ]\n)")]
        [InlineData("Type(RecordOf(Accounts))", "Type(RecordOf(Accounts))")]
        public void TestPrettyPrint(string script, string expected)
        {
            // Act & Assert
            var result = Format(script);
            Assert.NotNull(result);
            Assert.Equal(expected, result);

            // Act & Assert: Ensure idempotence
            result = Format(result);
            Assert.NotNull(result);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("x= /* test */ 1;", "x = /* test */1;")]
        [InlineData("y =3 /* test */;", "y = 3/* test */;")]
        [InlineData("F(ax: Number /* testttt */ , ab: Number): Number = ax* ab+y   + 2 + x;", "F(ax: Number /* testttt */ , ab: Number): Number = ax * ab + y + 2 + x;")]
        [InlineData("X():Void = { /* test */ Notify(\"SADF\"); /* asfddsf */ Notify(\"ASDFSFD\"); /* hi */ };", "X():Void = \n{\n\t/* test */Notify(\"SADF\");\n    /* asfddsf */Notify(\"ASDFSFD\");\n    /* hi */\n};")]
        [InlineData("x= /* test */ 1; y =3 /* test */; F(ax: Number /* testttt */ , ab: Number): Number = ax* ab+y   + 2 + x;X():Void = { /* test */ Notify(\"SADF\"); /* asfddsf */ Notify(\"ASDFSFD\"); /* hi */ };", "x = /* test */1;\ny = 3/* test */;\nF(ax: Number /* testttt */ , ab: Number): Number = ax * ab + y + 2 + x;\nX():Void = \n{\n\t/* test */Notify(\"SADF\");\n    /* asfddsf */Notify(\"ASDFSFD\");\n    /* hi */\n};")]
        [InlineData("T := Type(Number);", "T := Type(Number);")]
        [InlineData("N = 5; /*Type Dec*/ T := Type([{name: Text, age: Number}]);", "N = 5;\n/*Type Dec*/ T := Type([\n    {\n        name: Text,\n        age: Number\n    }\n]);")]
        [InlineData("T := Type/*com*/( /*com*/ Number /*com*/) /*com*/;", "T := Type/*com*/( /*com*/Number /*com*/)/*com*/;")]
        [InlineData("T := Type(/*RecordOf*/ RecordOf(Accounts));", "T := Type(/*RecordOf*/RecordOf(Accounts));")]
        public void TestUserDefinitionsPrettyPrint(string script, string expected)
        {
            var parserOptions = new ParserOptions()
            {
                AllowsSideEffects = true
            };

            // Act & Assert
            var result = FormatUserDefinitions(script, parserOptions, Features.PowerFxV1);
            Assert.NotNull(result);
            Assert.Equal(expected, result);

            // Act & Assert: Ensure idempotence
            result = FormatUserDefinitions(result, parserOptions, Features.PowerFxV1);
            Assert.NotNull(result);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(TexlLexer.ReservedBlank)]
        [InlineData(TexlLexer.ReservedChild)]
        [InlineData(TexlLexer.ReservedChildren)]
        [InlineData(TexlLexer.ReservedEmpty)]
        [InlineData(TexlLexer.ReservedIs)]
        [InlineData(TexlLexer.ReservedNone)]
        [InlineData(TexlLexer.ReservedNothing)]
        [InlineData(TexlLexer.ReservedNull)]
        [InlineData(TexlLexer.ReservedSiblings)]
        [InlineData(TexlLexer.ReservedThis)]
        [InlineData(TexlLexer.ReservedUndefined)]
        public void TestPrettyPrintWithDisabledReservedKeywordsFlag(string keyword)
        {
            // Arrange
            var expression = $"Set({keyword}; true)";
            var expectedFormattedExpr = $"Set(\n    {keyword};\n    true\n)";
            var flags = Flags.DisableReservedKeywords | Flags.EnableExpressionChaining;

            // Act
            var result = Format(expression, flags);

            // Asssert
            Assert.NotNull(result);
            Assert.Equal(expectedFormattedExpr, result);

            // Act: Ensure idempotence
            result = Format(result, flags);

            // Assert: Ensure idempotence
            Assert.NotNull(result);
            Assert.Equal(expectedFormattedExpr, result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(10)]
        public void TestPrettyPrintAndRemoveWhitespaceRoundtripWithDisabledReservedKeywordsFlag(int trips)
        {
            // Arrange
            var unformattedExpr = $"Set({TexlLexer.ReservedChildren}; true )";
            var formatedExpr = $"Set(\n    {TexlLexer.ReservedChildren};\n    true\n)";
            var expectedOutcome = trips % 2 == 0 ? unformattedExpr : formatedExpr;

            // Act
            var outcome = unformattedExpr;
            for (var i = 1; i <= trips; ++i)
            {
                outcome = i % 2 == 0 ?
                          TexlLexer.InvariantLexer.RemoveWhiteSpace(outcome) :
                          Format(outcome, Flags.DisableReservedKeywords | Flags.EnableExpressionChaining);
            }

            // Assert
            Assert.Equal(expectedOutcome, outcome);
        }

        // Testing for hosts to provide a custom way to print the structural representation of an expression.
        [Theory]
        [InlineData("With({myfield:firstNameControl.Text}, myfield & \"something\")", "With({ #$fieldname$#:TextInputControl.#$righthandid$# }, #$LambdaField$# & #$string$#)")]
        public void CustomStructuralPrintTest(string expression, string expected)
        {
            var engine = new Engine();
            var result = engine.Check(expression);

            var symbolTable = new SymbolTable();
            symbolTable.AddVariable("score", FormulaType.Decimal);

            var check = new CheckResult(new Engine())
                .SetText("1+Sum(score, missing)")
                .SetBindingInfo(symbolTable);

            var errors = check.ApplyErrors();
            var parse = check.ApplyParse();

            var log = result.ApplyGetLogging(new CustomStructuralPrintSanitizer());

            Assert.Equal(expected, log);
        }

        [Fact]
        public void CustomStructuralPrintSanitizerTest()
        {
            var engine = new Engine();
            var expression = "With({myfield:firstNameControl.Text}, myfield & \"something\")";

            var result = engine.Check(expression);

            var symbolTable = new SymbolTable();
            symbolTable.AddVariable("score", FormulaType.Decimal);

            var check = new CheckResult(new Engine())
                .SetText("1+Sum(score, missing)")
                .SetBindingInfo(symbolTable);

            var errors = check.ApplyErrors();
            var parse = check.ApplyParse();

            var mapping1 = new Dictionary<string, string>()
            {
                { "firstNameControl", "Mapping1" }
            };

            var logging = result.ApplyGetLogging(new CustomStructuralPrintSanitizer(mapping1));
            Assert.Equal("With({ #$fieldname$#:Mapping1.#$righthandid$# }, #$LambdaField$# & #$string$#)", logging);

            var mapping2 = new Dictionary<string, string>()
            {
                { "firstNameControl", "Mapping2" }
            };

            logging = result.ApplyGetLogging(new CustomStructuralPrintSanitizer(mapping2));
            Assert.Equal("With({ #$fieldname$#:Mapping2.#$righthandid$# }, #$LambdaField$# & #$string$#)", logging);

            var mapping3 = new Dictionary<string, string>()
            {
                { "firstNameControl", "Mapping3" }
            };

            logging = result.ApplyGetLogging(new CustomStructuralPrintSanitizer(mapping3));
            Assert.Equal("With({ #$fieldname$#:Mapping3.#$righthandid$# }, #$LambdaField$# & #$string$#)", logging);
        }

        /// <summary>
        /// This simulates a host that provides a map of control names and their sanitized names.
        /// </summary>
        internal class CustomStructuralPrintSanitizer : ISanitizedNameProvider
        {
            private readonly IReadOnlyDictionary<string, string> _mapping;

            public CustomStructuralPrintSanitizer(IReadOnlyDictionary<string, string> mapping)
            {
                _mapping = mapping;
            }

            public CustomStructuralPrintSanitizer()
            {
                _mapping = new Dictionary<string, string>()
                {
                    { "firstNameControl", "TextInputControl" }
                };
            }

            public bool TrySanitizeIdentifier(Identifier identifier, out string sanitizedName, DottedNameNode dottedNameNode = null)
            {
                return _mapping.TryGetValue(identifier.Name.Value, out sanitizedName);
            }
        }
    }
}
