using System.Collections.Generic;
using Brisk.ViewModels;
using Xunit;

namespace Brisk.Tests;

file sealed class SampleVm : ViewModelBase
{
    private int _count;
    public int Count { get => _count; set => Set(ref _count, value); }
}

public class ViewModelBaseTests
{
    [Fact]
    public void Set_RaisesPropertyChanged_OnlyOnRealChange()
    {
        var vm = new SampleVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Count = 5;
        vm.Count = 5;
        Assert.Equal(new[] { "Count" }, raised);
    }

    [Fact]
    public void RelayCommand_ExecutesAndGates()
    {
        var ran = 0;
        var allowed = false;
        var cmd = new RelayCommand(() => ran++, () => allowed);

        Assert.False(cmd.CanExecute(null));
        allowed = true;
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void RelayCommand_RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand(() => { });
        var fired = 0;
        cmd.CanExecuteChanged += (_, _) => fired++;
        cmd.RaiseCanExecuteChanged();
        Assert.Equal(1, fired);
    }
}
