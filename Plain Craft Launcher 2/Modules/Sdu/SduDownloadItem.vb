Imports Newtonsoft.Json.Linq
Imports System.Globalization

''' <summary>
''' SDUcraft 下载页中的一条下载讯息。
''' </summary>
Public Class SduDownloadItem

    Private Const MaxItemCount As Integer = 1000
    Private Const MaxIdLength As Integer = 128
    Private Const MaxNameLength As Integer = 200
    Private Const MaxVersionLength As Integer = 80
    Private Const MaxDateLength As Integer = 48
    Private Const MaxIntroLength As Integer = 4096

    ''' <summary>官网项目唯一标识。</summary>
    Public Property Id As String = String.Empty

    ''' <summary>下载项名称。</summary>
    Public Property Name As String = "未命名下载"

    ''' <summary>界面显示名称，隐藏压缩包扩展名；原始名称保留供安装项识别使用。</summary>
    Public ReadOnly Property DisplayName As String
        Get
            Return FormatDisplayName(Name)
        End Get
    End Property

    Friend Shared Function FormatDisplayName(Value As String) As String
        Dim Name = If(Value, String.Empty).Trim()
        For Each Extension In {".tar.gz", ".tar.bz2", ".tar.xz", ".zip", ".mrpack", ".7z", ".rar", ".tar", ".tgz"}
            If Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) Then
                Dim Title = Name.Substring(0, Name.Length - Extension.Length).TrimEnd()
                Return If(Title.Length = 0, Name, Title)
            End If
        Next
        Return Name
    End Function

    ''' <summary>适配的游戏版本，可能为空。</summary>
    Public Property Version As String = String.Empty

    ''' <summary>发布日期，格式由站点提供。</summary>
    Public Property PublishDate As String = String.Empty

    ''' <summary>最后修改日期，可能为空。</summary>
    Public Property ModifyDate As String = String.Empty

    Public ReadOnly Property LatestTime As DateTimeOffset?
        Get
            Return NewestTime(ModifyDate, PublishDate)
        End Get
    End Property

    Public ReadOnly Property IsNew As Boolean
        Get
            Return IsRecent(LatestTime, DateTimeOffset.UtcNow)
        End Get
    End Property

    Friend Shared Function IsRecent(Time As DateTimeOffset?, Now As DateTimeOffset) As Boolean
        Return Time.HasValue AndAlso Time.Value <= Now AndAlso Now - Time.Value <= TimeSpan.FromDays(7)
    End Function

    ''' <summary>站点返回的原始介绍 HTML。</summary>
    Public Property IntroHtml As String = String.Empty

    ''' <summary>去掉 HTML 标签后的纯文本介绍。</summary>
    Public ReadOnly Property Intro As String
        Get
            Return RemoveHtml(IntroHtml)
        End Get
    End Property

    ''' <summary>解析站点返回的完整 JSON 数组。</summary>
    Public Shared Function ParseList(Json As String) As List(Of SduDownloadItem)
        Return JArray.Parse(Json).
            OfType(Of JObject)().
            Select(AddressOf FromJson).
            OrderByDescending(Function(Item) Item.IsNew).
            ThenByDescending(Function(Item) Item.LatestTime).
            Take(MaxItemCount).
            ToList()
    End Function

    Friend Shared Function NewestTime(ParamArray Values As String()) As DateTimeOffset?
        Dim Result As DateTimeOffset? = Nothing
        For Each Value In Values
            If String.IsNullOrWhiteSpace(Value) Then Continue For
            Dim Parsed As DateTimeOffset
            If System.Text.RegularExpressions.Regex.IsMatch(Value.Trim(), "(?:Z|[+-]\d{2}:?\d{2})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) Then
                If Not DateTimeOffset.TryParse(Value, CultureInfo.InvariantCulture, DateTimeStyles.None, Parsed) Then Continue For
            Else
                Dim DateValue As DateTime
                If Not DateTime.TryParse(Value, CultureInfo.InvariantCulture, DateTimeStyles.None, DateValue) Then Continue For
                Parsed = New DateTimeOffset(DateTime.SpecifyKind(DateValue, DateTimeKind.Unspecified), TimeSpan.FromHours(8))
            End If
            If Not Result.HasValue OrElse Parsed > Result.Value Then Result = Parsed
        Next
        Return Result
    End Function

    ''' <summary>把单个 JSON 对象转换为界面可用的数据模型。</summary>
    Public Shared Function FromJson(Item As JObject) As SduDownloadItem
        Return New SduDownloadItem With {
            .Id = ReadId(Item),
            .Name = If(String.IsNullOrWhiteSpace(ReadString(Item, "name")), "未命名下载", ReadBoundedString(Item, "name", MaxNameLength)),
            .Version = ReadBoundedString(Item, "version", MaxVersionLength),
            .PublishDate = ReadBoundedString(Item, "publish_date", MaxDateLength),
            .ModifyDate = ReadBoundedString(Item, "modify_date", MaxDateLength),
            .IntroHtml = ReadBoundedString(Item, "intro", MaxIntroLength)
        }
    End Function

    Private Shared Function ReadBoundedString(Item As JObject, PropertyName As String, MaxLength As Integer) As String
        Dim Value = ReadString(Item, PropertyName)
        Return If(Value.Length <= MaxLength, Value, Value.Substring(0, MaxLength))
    End Function

    Private Shared Function ReadId(Item As JObject) As String
        Dim Value = ReadBoundedString(Item, "id", MaxIdLength)
        Dim IsValid = System.Text.RegularExpressions.Regex.IsMatch(Value, "^[A-Za-z0-9_.:-]+$")
        Return If(IsValid, Value, String.Empty)
    End Function

    Private Shared Function ReadString(Item As JObject, PropertyName As String) As String
        If Item Is Nothing Then Return String.Empty
        Dim Value = Item(PropertyName)
        Return If(Value Is Nothing, String.Empty, Value.ToString())
    End Function

    Private Shared Function RemoveHtml(Text As String) As String
        If String.IsNullOrWhiteSpace(Text) Then Return String.Empty

        Dim PlainText = System.Text.RegularExpressions.Regex.Replace(Text, "<[^>]+>", " ")
        PlainText = PlainText.
            Replace("&nbsp;", " ").
            Replace("&amp;", "&").
            Replace("&lt;", "<").
            Replace("&gt;", ">").
            Replace("&quot;", """")

        Return System.Text.RegularExpressions.Regex.Replace(PlainText, "\s+", " ").Trim()
    End Function

End Class
