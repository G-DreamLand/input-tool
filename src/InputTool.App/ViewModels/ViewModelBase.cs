using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InputTool.App.ViewModels;

/// <summary>
/// ViewModel 基类：只负责属性变更通知，不做任何 IO 或计算（§6.2 数据流约束）。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发属性变更通知。</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>设置字段值并在变化时触发通知。</summary>
    /// <returns>值是否发生变化。</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
