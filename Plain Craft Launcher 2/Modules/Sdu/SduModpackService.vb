Imports Newtonsoft.Json.Linq

''' <summary>
''' SDUcraft 官网游戏下载数据的类型化访问层。
''' </summary>
Public Class SduGameDownload

    Public Property Id As String = String.Empty
    Public Property Name As String = "未命名整合包"
    Public Property Version As String = String.Empty
    Public Property Author As String = String.Empty
    Public Property PublishDate As String = String.Empty
    Public Property ModifyDate As String = String.Empty
    Public Property IntroHtml As String = String.Empty
    Public Property Files As New List(Of SduGameDownloadFile)

    ''' <summary>安装时优先使用的文件；当前启动器运行在 Windows 上。</summary>
    Public ReadOnly Property PreferredFile As SduGameDownloadFile
        Get
            Return Files.FirstOrDefault(
                Function(File) String.Equals(File.OperatingSystem, "Windows", StringComparison.OrdinalIgnoreCase))
        End Get
    End Property

End Class

''' <summary>
''' SDUcraft 官网上的一个可下载文件。
''' </summary>
Public Class SduGameDownloadFile

    Public Property Id As String = String.Empty
    Public Property DownloadId As String = String.Empty
    Public Property OperatingSystem As String = "Windows"
    Public Property SizeInMegabytes As Double
    Public Property FileUrl As String = String.Empty
    Public Property ModifyDate As String = String.Empty

End Class

''' <summary>
''' 从 SDUcraft API 读取游戏下载列表，并把下载项与文件集合关联。
''' </summary>
Public Module SduModpackService

    Private Const DownloadApiUrl As String = "https://www.sducraft.top/api/download/"
    Private Const FileSetApiUrl As String = "https://www.sducraft.top/api/filesetfordownload/"
    Private Const MaxResponseLength As Integer = 2 * 1024 * 1024
    Private Const AllowedHost As String = "www.sducraft.top"

    ''' <summary>获取包含有效可安装文件的整合包列表。</summary>
    Public Function FetchModpacks() As List(Of SduGameDownload)
        Dim Downloads = ParseDownloads(RequestJson(DownloadApiUrl))
        Dim Files = ParseFiles(RequestJson(FileSetApiUrl))

        For Each Download In Downloads
            Download.Files = Files.Where(Function(File) File.DownloadId = Download.Id).ToList()
        Next

        '只显示官网上提供可安装压缩包的项目；纯介绍项或没有 Windows 文件的历史项目不会误装。
        Return Downloads.Where(Function(Download) Download.PreferredFile IsNot Nothing).ToList()
    End Function

    Private Function ParseDownloads(Json As String) As List(Of SduGameDownload)
        Return JArray.Parse(Json).
            OfType(Of JObject)().
            Select(AddressOf FromDownload).
            ToList()
    End Function

    Private Function ParseFiles(Json As String) As List(Of SduGameDownloadFile)
        Return JArray.Parse(Json).
            OfType(Of JObject)().
            Select(AddressOf FromFile).
            Where(Function(File) File IsNot Nothing).
            ToList()
    End Function

    Private Function FromDownload(Item As JObject) As SduGameDownload
        Return New SduGameDownload With {
            .Id = ReadId(Item, "id"),
            .Name = If(String.IsNullOrWhiteSpace(ReadString(Item, "name")), "未命名整合包", ReadBoundedString(Item, "name", 200)),
            .Version = ReadBoundedString(Item, "version", 80),
            .Author = ReadBoundedString(Item, "author", 120),
            .PublishDate = ReadBoundedString(Item, "publish_date", 32),
            .ModifyDate = ReadBoundedString(Item, "modify_date", 32),
            .IntroHtml = ReadBoundedString(Item, "intro", 4096)
        }
    End Function

    Private Function FromFile(Item As JObject) As SduGameDownloadFile
        If Item Is Nothing Then Return Nothing

        Dim Url = ReadString(Item, "file")
        If Not IsAllowedDownloadUrl(Url) Then
            Logger.Warn("已忽略不符合 SDUcraft 官网 HTTPS 直链与文件类型要求的下载项")
            Return Nothing
        End If

        Return New SduGameDownloadFile With {
            .Id = ReadId(Item, "id"),
            .DownloadId = ReadId(Item, "download"),
            .OperatingSystem = If(String.IsNullOrWhiteSpace(ReadString(Item, "OS")), "Windows", ReadBoundedString(Item, "OS", 24)),
            .SizeInMegabytes = ReadDouble(Item, "size"),
            .FileUrl = Url,
            .ModifyDate = ReadBoundedString(Item, "modify_date", 32)
        }
    End Function

    ''' <summary>
    ''' 远端返回的下载地址必须仍是官网 HTTPS 直链，避免站点数据被篡改时触发 SSRF 或下载任意主机文件。
    ''' </summary>
    Private Function IsAllowedDownloadUrl(Value As String) As Boolean
        If String.IsNullOrWhiteSpace(Value) Then Return False

        Dim Url As Uri = Nothing
        If Not Uri.TryCreate(Value, UriKind.Absolute, Url) Then Return False
        If Url.Scheme <> Uri.UriSchemeHttps Then Return False
        If Not String.Equals(Url.Host, AllowedHost, StringComparison.OrdinalIgnoreCase) Then Return False
        If Not String.IsNullOrEmpty(Url.UserInfo) OrElse Not String.IsNullOrEmpty(Url.Fragment) Then Return False
        '不区分大小写，避免服务端路径大小写变动导致合法下载项被误拦截
        If Not Url.AbsolutePath.StartsWithF("/FileSetForDownload/", True) Then Return False

        Dim Extension = PathUtils.GetExtension(Url.AbsolutePath).Lower()
        Return Extension = "zip" OrElse Extension = "mrpack"
    End Function

    Private Function RequestJson(Url As String) As String
        Dim Response = NetRequestByClientRetry(Url, RequireJson:=True)
        If Response.Length > MaxResponseLength Then
            Throw New InvalidOperationException($"SDUcraft API 响应超过安全上限：{Response.Length} 字符")
        End If
        Return Response
    End Function

    Private Function ReadId(Item As JObject, PropertyName As String) As String
        If Item Is Nothing Then Return String.Empty

        Dim Value = Item(PropertyName)
        If Value Is Nothing Then Return String.Empty

        Dim Text = Value.ToString().Trim()
        If Text.Length = 0 OrElse Text.Length > 128 Then Return String.Empty
        If Not System.Text.RegularExpressions.Regex.IsMatch(Text, "^[A-Za-z0-9_.:-]+$") Then Return String.Empty
        Return Text
    End Function

    Private Function ReadBoundedString(Item As JObject, PropertyName As String, MaxLength As Integer) As String
        Dim Value = ReadString(Item, PropertyName)
        Return If(Value.Length <= MaxLength, Value, Value.Substring(0, MaxLength))
    End Function

    Private Function ReadString(Item As JObject, PropertyName As String) As String
        If Item Is Nothing Then Return String.Empty
        Dim Value = Item(PropertyName)
        Return If(Value Is Nothing, String.Empty, Value.ToString())
    End Function

    Private Function ReadDouble(Item As JObject, PropertyName As String) As Double
        Dim Value = Item?.Value(Of Double?)(PropertyName)
        Return If(Value.HasValue AndAlso Not Double.IsNaN(Value.Value) AndAlso Not Double.IsInfinity(Value.Value), Value.Value, 0)
    End Function

End Module
