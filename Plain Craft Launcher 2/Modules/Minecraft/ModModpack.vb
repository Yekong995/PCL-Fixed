Public Module ModModpack

    '触发整合包安装的外部接口
    ''' <summary>
    ''' 弹窗要求选择一个整合包文件并进行安装。
    ''' </summary>
    Public Sub ModpackInstall()
        PclModpack.ModpackInstall()
    End Sub
    ''' <summary>
    ''' 构建并启动安装给定的整合包文件的加载器，并返回该加载器。若失败则抛出异常。
    ''' 必须在工作线程执行。
    ''' </summary>
    Public Function ModpackInstall(File As String, Optional InstanceName As String = Nothing, Optional Logo As String = Nothing) As LoaderCombo(Of String)
        Return PclModpack.ModpackInstall(File, InstanceName, Logo)
    End Function

End Module
