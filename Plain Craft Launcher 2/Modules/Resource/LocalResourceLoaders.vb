Public Module LocalResourceLoaders
    Public ReadOnly Property LocalResourceLoader As LoaderTask(Of String, List(Of LocalResourceFile))
        Get
            Return PCLCS.Modules.LocalResourceLoaders.LocalResourceLoader
        End Get
    End Property
    Public ReadOnly Property LocalResourceOnlineLoader As LoaderTask(Of (ModList As List(Of LocalResourceFile), Cache As JObject, Target As McVersion), Integer)
        Get
            Return PCLCS.Modules.LocalResourceLoaders.LocalResourceOnlineLoader
        End Get
    End Property
End Module
