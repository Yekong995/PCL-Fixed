'本文件的逻辑已迁移至 PCLCS\Modules\ModEvent.cs（ModEvent / CustomEvent / CustomEventService / CustomEventCollection）。
'为兼容 XAML 中的 local:CustomEventService / local:CustomEventCollection / local:CustomEvent 引用
'以及既有 VB 代码中的非限定引用，此处保留 PCL 命名空间下的同名类型转发壳（继承 C# 实现）与扩展方法。

#Region "附加属性"

''' <summary>
''' 用于在 XAML 中初始化列表对象。
''' 附加属性无法在 XAML 中为每个对象初始化独立的列表对象，因此需要一个包装类，然后在 XAML 中显式初始化。
''' （转发壳：实现位于 PCLCS.Modules.CustomEventCollection）
''' </summary>
<Markup.ContentProperty("Events")>
Public Class CustomEventCollection
    Inherits PCLCS.Modules.CustomEventCollection
End Class

''' <summary>
''' 提供自定义事件的附加属性。
''' （转发壳：实现位于 PCLCS.Modules.CustomEventService）
''' </summary>
Public Class CustomEventService
    Inherits PCLCS.Modules.CustomEventService
End Class

''' <summary>
''' 自定义事件。
''' （转发壳：实现位于 PCLCS.Modules.CustomEvent）
''' </summary>
Public Class CustomEvent
    Inherits PCLCS.Modules.CustomEvent
End Class

Partial Public Module ModMain

    ''' <summary>
    ''' 触发该控件上的自定义事件。
    ''' 事件会在新线程中执行。
    ''' </summary>
    <Runtime.CompilerServices.Extension>
    Public Sub RaiseCustomEvent(Control As DependencyObject)
        PCLCS.Modules.ModEvent.RaiseCustomEvent(Control)
    End Sub

End Module

#End Region
