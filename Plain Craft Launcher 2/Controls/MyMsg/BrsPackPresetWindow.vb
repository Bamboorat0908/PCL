Imports System.Windows.Markup
Imports System.Windows.Automation

Public Class BrsPackPresetWindow
    Inherits Window

    Public ReadOnly Property LowPreset As Boolean
        Get
            Return LowChoice.IsChecked = True
        End Get
    End Property
    Private ReadOnly HighChoice As RadioButton
    Private ReadOnly LowChoice As RadioButton
    Private ReadOnly SelectionLabel As TextBlock

    Public Sub New(InstallRoot As String)
        Title = "选择整合包预设 · TRM 1.11"
        Width = Math.Min(660, SystemParameters.WorkArea.Width - 32)
        Height = If(Width < 520, 710, 520)
        MaxHeight = SystemParameters.WorkArea.Height - 32
        WindowStyle = WindowStyle.None
        ResizeMode = ResizeMode.NoResize
        ShowInTaskbar = False
        WindowStartupLocation = WindowStartupLocation.CenterScreen
        FontFamily = New FontFamily("Microsoft YaHei UI")
        UseLayoutRounding = True
        SnapsToDevicePixels = True
        Dim Light = Settings.Get(Of Integer)("UiThemeMode") = 1
        Dim View = <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" BorderThickness="1" BorderBrush="{DynamicResource PackLine}" Background="{DynamicResource PackSurface}">
                       <Border.Resources>
                           <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource PackInk}"/></Style>
                           <Style x:Key="Action" TargetType="Button">
                               <Setter Property="Foreground" Value="{DynamicResource PackInk}"/>
                               <Setter Property="Background" Value="{DynamicResource PackCard}"/>
                               <Setter Property="BorderBrush" Value="{DynamicResource PackLine}"/>
                               <Setter Property="BorderThickness" Value="1"/>
                               <Setter Property="Padding" Value="18,10"/>
                               <Setter Property="FontSize" Value="13"/>
                               <Setter Property="Cursor" Value="Hand"/>
                               <Setter Property="Template">
                                   <Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Shell" CornerRadius="3" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Shell" Property="Opacity" Value="0.84"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Shell" Property="BorderBrush" Value="{DynamicResource PackInk}"/><Setter TargetName="Shell" Property="BorderThickness" Value="2"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value>
                               </Setter>
                           </Style>
                           <Style x:Key="Preset" TargetType="RadioButton">
                               <Setter Property="Foreground" Value="{DynamicResource PackInk}"/>
                               <Setter Property="Cursor" Value="Hand"/>
                               <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                               <Setter Property="Template"><Setter.Value>
                                   <ControlTemplate TargetType="RadioButton">
                                       <Border x:Name="Card" CornerRadius="5" Background="{DynamicResource PackCard}" BorderBrush="{DynamicResource PackLine}" BorderThickness="1" Padding="20">
                                           <Grid><ContentPresenter/><Border x:Name="Mark" Width="20" Height="20" CornerRadius="10" BorderThickness="1" BorderBrush="{DynamicResource PackMuted}" HorizontalAlignment="Right" VerticalAlignment="Top"><TextBlock x:Name="Tick" Text="✓" Visibility="Collapsed" FontSize="12" FontWeight="Bold" Foreground="#20231E" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></Grid>
                                       </Border>
                                       <ControlTemplate.Triggers>
                                           <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource PackMuted}"/></Trigger>
                                           <Trigger Property="IsChecked" Value="True"><Setter TargetName="Card" Property="BorderBrush" Value="#E6CE49"/><Setter TargetName="Card" Property="Background" Value="{DynamicResource PackSelected}"/><Setter TargetName="Mark" Property="Background" Value="#E6CE49"/><Setter TargetName="Mark" Property="BorderBrush" Value="#E6CE49"/><Setter TargetName="Tick" Property="Visibility" Value="Visible"/></Trigger>
                                           <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Card" Property="BorderThickness" Value="2"/></Trigger>
                                       </ControlTemplate.Triggers>
                                   </ControlTemplate>
                               </Setter.Value></Setter>
                           </Style>
                       </Border.Resources>
                       <Grid>
                           <Grid.RowDefinitions><RowDefinition Height="3"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                           <Border Background="#E6CE49" HorizontalAlignment="Left" Width="76"/>
                           <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                               <StackPanel Margin="26,22,26,20">
                                   <Grid x:Name="Header"><TextBlock Text="BRS / CLIENT SETUP" FontFamily="Consolas" FontSize="11" Foreground="{DynamicResource PackMuted}" VerticalAlignment="Center"/><Button x:Name="CloseButton" Style="{StaticResource Action}" Content="×" FontSize="20" Padding="8,0" BorderThickness="0" Background="Transparent" HorizontalAlignment="Right" ToolTip="关闭 (Esc)"/></Grid>
                                   <StackPanel Orientation="Horizontal" Margin="0,12,0,0"><TextBlock Text="选择整合包预设" FontSize="25" FontWeight="SemiBold" Foreground="{DynamicResource PackInk}"/><Border Background="{DynamicResource PackCard}" CornerRadius="3" Padding="8,4" Margin="12,0,0,0" VerticalAlignment="Center"><TextBlock Text="TRM 1.11" FontSize="11" FontFamily="Consolas" Foreground="{DynamicResource PackMuted}"/></Border></StackPanel>
                                   <TextBlock Text="相同的冒险，不同的画面配置。" FontSize="13" Margin="0,8,0,22" Foreground="{DynamicResource PackMuted}"/>
                                   <Grid x:Name="Cards">
                                       <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
                                       <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                                       <RadioButton x:Name="HighChoice" Style="{StaticResource Preset}" GroupName="Preset" Margin="0,0,7,0">
                                           <StackPanel><TextBlock Text="01 / VISUAL" FontFamily="Consolas" FontSize="10" Foreground="{DynamicResource PackMuted}" Margin="0,0,0,16"/><TextBlock Text="高预设" FontSize="22" FontWeight="SemiBold"/><TextBlock Text="光影视觉" FontSize="12" Foreground="{DynamicResource PackMuted}" Margin="0,5,0,18"/>
                                               <Grid Margin="0,0,0,9"><TextBlock Text="光影" FontSize="12" Foreground="{DynamicResource PackMuted}"/><TextBlock Text="默认开启" FontSize="12" HorizontalAlignment="Right"/></Grid>
                                               <Grid><TextBlock Text="草地渲染效果" FontSize="12" Foreground="{DynamicResource PackMuted}"/><TextBlock Text="开启" FontSize="12" HorizontalAlignment="Right"/></Grid>
                                               <Border Height="1" Background="{DynamicResource PackLine}" Margin="0,17,0,12"/><TextBlock Text="164.4 MB" FontFamily="Consolas" FontSize="12" Foreground="{DynamicResource PackMuted}"/>
                                           </StackPanel>
                                       </RadioButton>
                                       <RadioButton x:Name="LowChoice" Grid.Column="1" Style="{StaticResource Preset}" GroupName="Preset" Margin="7,0,0,0">
                                           <StackPanel><TextBlock Text="02 / LITE" FontFamily="Consolas" FontSize="10" Foreground="{DynamicResource PackMuted}" Margin="0,0,0,16"/><TextBlock Text="低预设" FontSize="22" FontWeight="SemiBold"/><TextBlock Text="轻量画面" FontSize="12" Foreground="{DynamicResource PackMuted}" Margin="0,5,0,18"/>
                                               <Grid Margin="0,0,0,9"><TextBlock Text="光影" FontSize="12" Foreground="{DynamicResource PackMuted}"/><TextBlock Text="默认关闭" FontSize="12" HorizontalAlignment="Right"/></Grid>
                                               <Grid><TextBlock Text="草地渲染效果" FontSize="12" Foreground="{DynamicResource PackMuted}"/><TextBlock Text="关闭" FontSize="12" HorizontalAlignment="Right"/></Grid>
                                               <Border Height="1" Background="{DynamicResource PackLine}" Margin="0,17,0,12"/><TextBlock Text="164.8 MB" FontFamily="Consolas" FontSize="12" Foreground="{DynamicResource PackMuted}"/>
                                           </StackPanel>
                                       </RadioButton>
                                   </Grid>
                                   <TextBlock Text="独立安装，保留已有版本与存档。" FontSize="12" Foreground="{DynamicResource PackMuted}" Margin="0,18,0,6"/>
                                   <TextBlock x:Name="InstallPath" FontSize="11" Foreground="{DynamicResource PackMuted}" TextTrimming="CharacterEllipsis"/>
                               </StackPanel>
                           </ScrollViewer>
                           <Border Grid.Row="2" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource PackLine}" Padding="26,16">
                               <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                                   <StackPanel VerticalAlignment="Center"><TextBlock x:Name="SelectionLabel" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource PackInk}"/><TextBlock Text="安装时将补全游戏与模组依赖" FontSize="10" Foreground="{DynamicResource PackMuted}" Margin="0,4,12,0"/></StackPanel>
                                   <Button x:Name="CancelButton" Grid.Column="1" Style="{StaticResource Action}" Content="取消" Margin="0,0,10,0"/>
                                   <Button x:Name="DownloadButton" Grid.Column="2" Style="{StaticResource Action}" Content="下载并安装  →" Foreground="#20231E" Background="#E6CE49" BorderBrush="#E6CE49" FontWeight="SemiBold"/>
                               </Grid>
                           </Border>
                       </Grid>
                   </Border>
        Dim Root = CType(XamlReader.Parse(View.ToString()), Border)
        Dim Colors = New Dictionary(Of String, String) From {
            {"PackSurface", If(Light, "#F4F5EF", "#1A2022")}, {"PackCard", If(Light, "#FFFFFF", "#222A2D")},
            {"PackInk", If(Light, "#202A2C", "#EDF0E9")}, {"PackMuted", If(Light, "#637071", "#A0ADAF")},
            {"PackLine", If(Light, "#D3DAD5", "#3A4547")}, {"PackSelected", If(Light, "#F8F5DF", "#2D3027")}}
        For Each Pair In Colors
            Root.Resources(Pair.Key) = New BrushConverter().ConvertFromString(Pair.Value)
        Next
        Content = Root
        HighChoice = CType(Root.FindName("HighChoice"), RadioButton)
        LowChoice = CType(Root.FindName("LowChoice"), RadioButton)
        SelectionLabel = CType(Root.FindName("SelectionLabel"), TextBlock)
        Dim Download = CType(Root.FindName("DownloadButton"), Button)
        Dim Cancel = CType(Root.FindName("CancelButton"), Button)
        Dim Dismiss = CType(Root.FindName("CloseButton"), Button)
        Dim CancelLabel As New TextBlock With {.Text = "取消"}
        CancelLabel.SetResourceReference(TextBlock.ForegroundProperty, "PackInk")
        Cancel.Content = CancelLabel
        Dim CloseLabel As New TextBlock With {.Text = "×", .FontSize = 20}
        CloseLabel.SetResourceReference(TextBlock.ForegroundProperty, "PackInk")
        Dismiss.Content = CloseLabel
        Download.Content = New TextBlock With {.Text = "下载并安装  →", .Foreground = Brushes.Black, .FontWeight = FontWeights.SemiBold}
        Download.IsDefault = True
        Cancel.IsCancel = True
        AutomationProperties.SetName(HighChoice, "高预设，默认开启光影和草地渲染效果")
        AutomationProperties.SetName(LowChoice, "低预设，默认关闭光影和草地渲染效果")
        AutomationProperties.SetName(Dismiss, "关闭预设选择")
        Dim PathLabel = CType(Root.FindName("InstallPath"), TextBlock)
        PathLabel.Text = "安装目录  " & IO.Path.Combine(InstallRoot, "versions")
        PathLabel.ToolTip = PathLabel.Text
        AddHandler HighChoice.Checked, Sub() UpdateSelection()
        AddHandler LowChoice.Checked, Sub() UpdateSelection()
        HighChoice.IsChecked = True
        AddHandler Download.Click, Sub() DialogResult = True
        AddHandler Cancel.Click, Sub() Close()
        AddHandler Dismiss.Click, Sub() Close()
        AddHandler PreviewKeyDown, Sub(Sender, E)
                                       If E.Key = Input.Key.Escape Then
                                           E.Handled = True
                                           Close()
                                       End If
                                   End Sub
        AddHandler CType(Root.FindName("Header"), Grid).MouseLeftButtonDown, Sub(Sender, E)
                                                                                 If Dismiss.IsMouseOver Then Return
                                                                                 If E.LeftButton = Input.MouseButtonState.Pressed Then DragMove()
                                                                             End Sub
        Dim Cards = CType(Root.FindName("Cards"), Grid)
        Dim Reflow As Action(Of Boolean) = Sub(Narrow As Boolean)
                                   Grid.SetColumn(LowChoice, If(Narrow, 0, 1))
                                   Grid.SetRow(LowChoice, If(Narrow, 1, 0))
                                   Grid.SetColumnSpan(HighChoice, If(Narrow, 2, 1))
                                   Grid.SetColumnSpan(LowChoice, If(Narrow, 2, 1))
                                   HighChoice.Margin = If(Narrow, New Thickness(0, 0, 0, 12), New Thickness(0, 0, 7, 0))
                                   LowChoice.Margin = If(Narrow, New Thickness(0), New Thickness(7, 0, 0, 0))
                                   SelectionLabel.Parent.SetValue(Grid.VisibilityProperty, If(Narrow, Visibility.Collapsed, Visibility.Visible))
                               End Sub
        AddHandler Root.SizeChanged, Sub(Sender, E) Reflow(E.NewSize.Width < 520)
        Reflow(Width < 520)
        If FrmMain IsNot Nothing AndAlso FrmMain.IsVisible Then
            Owner = FrmMain
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        End If
    End Sub

    Private Sub UpdateSelection()
        SelectionLabel.Text = "已选择 · " & If(LowPreset, "低预设", "高预设")
    End Sub
End Class
