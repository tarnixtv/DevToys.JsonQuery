using CliWrap;

using Devlooped;

using DevToys.Api;

using Microsoft.Extensions.Logging;

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using static DevToys.Api.GUI;

namespace DevToys.JsonQuery;

[Export(typeof(IGuiTool))]
[Name("JsonQuery")]
[ToolDisplayInformation(
    IconFontName = "jq-icon",
    IconGlyph = '\uE900',
    GroupName = PredefinedCommonToolGroupNames.Converters,
    ResourceManagerAssemblyIdentifier = nameof(JsonQueryAssemblyIdentifier),
    ResourceManagerBaseName = "DevToys.JsonQuery.JsonQueryResources",
    ShortDisplayTitleResourceName = nameof(JsonQueryResources.ShortDisplayTitle),
    LongDisplayTitleResourceName = nameof(JsonQueryResources.LongDisplayTitle),
    DescriptionResourceName = nameof(JsonQueryResources.Description),
    AccessibleNameResourceName = nameof(JsonQueryResources.AccessibleName))]
[AcceptedDataTypeName(PredefinedCommonDataTypeNames.Json)]
internal sealed class JsonQueryGuiTool : IGuiTool
{
    internal enum Indentation
    {
        TwoSpaces,
        FourSpaces,
        OneTab,
        Minified
    }

    private static readonly SettingDefinition<Indentation> indentationMode
        = new(name: $"{nameof(JsonQueryGuiTool)}.{nameof(indentationMode)}", defaultValue: Indentation.TwoSpaces);

    private static readonly SettingDefinition<bool> sortKeys
        = new(name: $"{nameof(JsonQueryGuiTool)}.{nameof(sortKeys)}", defaultValue: false);

    private enum GridRows
    {
        Top,
        Middle,
        Bottom,
    }

    private enum GridColumns
    {
        Stretch
    }

    private readonly DisposableSemaphore _semaphore = new();
    private readonly ILogger _logger;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IUIMultiLineTextInput _inputJson = MultiLineTextInput("input-json-json-query-tester");
    private readonly IUISingleLineTextInput _inputJsonQuery = SingleLineTextInput("input-json-json-query-tester");
    private readonly IUIMultiLineTextInput _outputJson = MultiLineTextInput("output-json-query-tester");
    private readonly IUILabel _errorLabel = Label();
    private readonly IUISettingGroup _outputFormatSettingGroup = SettingGroup();
    private readonly IUISetting _whitespaceSetting = Setting();
    private readonly IUISetting _indentSetting = Setting();


    private CancellationTokenSource? _cancellationTokenSource;

    [ImportingConstructor]
    public JsonQueryGuiTool(ISettingsProvider settingsProvider)
    {
        _logger = this.Log();
        _settingsProvider = settingsProvider;

        var previous = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");

        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

        _logger.LogInformation($"Initialized jq: {JQ.Path}");

        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", previous);
    }

    // For unit tests.
    internal Task? WorkTask { get; private set; }
    public UIToolView View
        => new(
            isScrollable: false,
            Grid()
                .ColumnSmallSpacing()

                .Rows(
                    (GridRows.Top, Auto),
                    (GridRows.Middle, new UIGridLength(1, UIGridUnitType.Fraction)))

                .Columns(
                    (GridColumns.Stretch, new UIGridLength(1, UIGridUnitType.Fraction)))

                .Cells(
                    Cell(
                        GridRows.Top,
                        GridColumns.Stretch,
                        Stack().Vertical().WithChildren(
                            Label().Text(JsonQueryResources.Configuration),
                            Setting("json-text-indentation-setting")
                                .Icon("FluentSystemIcons", '\uF6F8')
                                .Title(JsonQueryResources.Indentation)
                                .Handle(
                                    _settingsProvider,
                                    indentationMode,
                                    OnIndentationModelChanged,
                                    Item(JsonQueryResources.TwoSpaces, Indentation.TwoSpaces),
                                    Item(JsonQueryResources.FourSpaces, Indentation.FourSpaces),
                                    Item(JsonQueryResources.OneTab, Indentation.OneTab),
                                    Item(JsonQueryResources.Minified, Indentation.Minified)
                                ),
                            Setting("json-text-sortKeys-setting")
                                .Icon("FluentSystemIcons", '\uf802')
                                .Title(JsonQueryResources.SortKeys)
                                .Handle(
                                    _settingsProvider,
                                    sortKeys,
                                    OnSortKeysChanged
                                )
                        )
                    ),
                    Cell(
                        GridRows.Middle,
                        GridColumns.Stretch,
                        SplitGrid()
                            .Vertical()
                            .LeftPaneLength(new UIGridLength(1, UIGridUnitType.Fraction))
                            .RightPaneLength(new UIGridLength(1, UIGridUnitType.Fraction))

                            .WithLeftPaneChild(
                                _inputJson
                                    .Title(JsonQueryResources.InputTitle)
                                    .Language("json")
                                    .OnTextChanged(OnInputJsonChanged))

                            .WithRightPaneChild(
                                SplitGrid()
                                    .Horizontal()
                                    .BottomPaneLength(new(375, UIGridUnitType.Pixel))

                                    .WithTopPaneChild(
                                        Grid()
                                            .ColumnSmallSpacing()

                                            .Rows(
                                                (GridRows.Top, Auto),
                                                (GridRows.Middle, Auto),
                                                (GridRows.Bottom, new UIGridLength(1, UIGridUnitType.Fraction)))

                                            .Columns(
                                                (GridColumns.Stretch, new UIGridLength(1, UIGridUnitType.Fraction)))

                                            .Cells(
                                                Cell(
                                                    GridRows.Top,
                                                    GridColumns.Stretch,

                                                    _inputJsonQuery
                                                        .Text(".")
                                                        .Title(JsonQueryResources.InputJsonQueryTitle)
                                                        .OnTextChanged(OnInputJsonQueryChanged)),

                                                Cell(
                                                    GridRows.Middle,
                                                    GridColumns.Stretch,

                                                    _errorLabel.Text("")),

                                                Cell(
                                                    GridRows.Bottom,
                                                    GridColumns.Stretch,

                                                    _outputJson
                                                        .ReadOnly()
                                                        .Extendable()
                                                        .Language("json")
                                                        .Title(JsonQueryResources.Output))))

                                    .WithBottomPaneChild(

                                        Grid()
                                            .ColumnSmallSpacing()

                                            .Rows(
                                                (GridRows.Top, new UIGridLength(1, UIGridUnitType.Fraction)),
                                                (GridRows.Middle, Auto))

                                            .Columns((GridColumns.Stretch, new UIGridLength(1, UIGridUnitType.Fraction)))

                                            .Cells(
                                                Cell(
                                                    GridRows.Top,
                                                    GridColumns.Stretch,

                                                    DataGrid()
                                                        .Title(JsonQueryResources.CheatSheetTitle)
                                                        .Extendable()
                                                        .WithColumns(JsonQueryResources.CheatSheetSyntax, JsonQueryResources.CheatSheetExample, JsonQueryResources.CheatSheetDescription)
                                                        .WithRows(
                                                            CheatSheetRow(@".", @".", JsonQueryResources.CheatSheetIdentity),
                                                            CheatSheetRow(@".field", @".name", JsonQueryResources.CheatSheetObjectIdentifierIndex),
                                                            CheatSheetRow(@".field?", @".name?", JsonQueryResources.CheatSheetOptionalObjectIdentifierIndex),
                                                            CheatSheetRow(@".[<string>]", @".[""name""]", JsonQueryResources.CheatSheetObjectIndex),
                                                            CheatSheetRow(@".[<number>]", @".[0]", JsonQueryResources.CheatSheetArrayIndex),
                                                            CheatSheetRow(@".[]", @".children | .[]", JsonQueryResources.CheatSheetArrayObjectValueIterator),
                                                            CheatSheetRow(@",", @".username, .email", JsonQueryResources.CheatSheetConcatenation),
                                                            CheatSheetRow(@"|", @".users[] | .email", JsonQueryResources.CheatSheetPipe),
                                                            CheatSheetRow(@"[...]", @"[ .children[] | .name ]", JsonQueryResources.CheatSheetArrayConstruction),
                                                            CheatSheetRow(@"{...}", @"{ login: .email, (.role): true }", JsonQueryResources.CheatSheetObjectConstruction)
                                                        )),

                                                Cell(
                                                    GridRows.Middle,
                                                    GridColumns.Stretch,

                                                    Button()
                                                        .HyperlinkAppearance()
                                                        .Text("Open jq manual")
                                                        .OnClick(OpenWebsite))))))));


    private void OnSortKeysChanged(bool sortKeys)
    {
        StartTest();
    }

    private void OnIndentationModelChanged(Indentation indentation)
    {
        StartTest();
    }

    private async void OpenWebsite()
    {
        if (OperatingSystem.IsWindows())
        {
            await Cli.Wrap("cmd")
                .WithArguments("/c start https://jqlang.org/manual/#basic-filters")
                .ExecuteAsync();
        }
        else
        {
            await Cli.Wrap("https://jqlang.org/manual/#basic-filters")
                .ExecuteAsync();
        }
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
        if (dataTypeName == PredefinedCommonDataTypeNames.Json && parsedData is string json)
        {
            _inputJson.Text(json);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _semaphore.Dispose();
    }

    private void OnInputJsonChanged(string json)
    {
        StartTest();
    }

    private void OnInputJsonQueryChanged(string jsonQuery)
    {
        StartTest();
    }

    private void StartTest()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        WorkTask = RunJqAsync(_inputJson.Text.ReplaceLineEndings(), _inputJsonQuery.Text, _cancellationTokenSource.Token);
    }

    private async Task RunJqAsync(string json, string query, CancellationToken cancellationToken)
    {
        using (await _semaphore.WaitAsync(cancellationToken))
        {
            await TaskSchedulerAwaiter.SwitchOffMainThreadAsync(cancellationToken);

            string? result = null;
            try
            {
                var indentMode = _settingsProvider.GetSetting(indentationMode);

                var jqParams = new JqParams(json, query)
                {
                    CompactOutput = indentMode == Indentation.Minified,
                    Indent = indentMode == Indentation.TwoSpaces ? 2 : indentMode == Indentation.FourSpaces ? 4 : null,
                    Tab = indentMode == Indentation.OneTab ? true : null,
                    SortKeys = _settingsProvider.GetSetting(sortKeys)
                };
                var jqResult = await JQ.ExecuteAsync(jqParams);

                _errorLabel.Text(jqResult.StandardError);

                if (jqResult.ExitCode == 0)
                {
                    result = jqResult.StandardOutput;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while running jq process.");
                Console.WriteLine(ex);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested && result is not null)
                {
                    _outputJson.Text(result);
                }
            }
        }
    }

    private static IUIDataGridRow CheatSheetRow(string syntax, string example, string description)
        => Row(
            null,
            details: null,
            Cell(Label().NeverWrap().Style(UILabelStyle.BodyStrong).Text($" {syntax} ")),
            Cell(Label().NeverWrap().Style(UILabelStyle.BodyStrong).Text($" {example} ")),
            Cell(Label().NeverWrap().Text($" {description} ")));
}