using System.Linq;
using DesktopBox.Models;
using DesktopBox.Services;
using DesktopBox.ViewModels;
using FluentAssertions;
using Moq;

namespace DesktopBox.Tests;

public class MainViewModelTests
{
    private readonly Mock<IPersistenceService> _store = new();
    private readonly Mock<IIconExtractorService> _icon = new();

    private MainViewModel NewVm()
    {
        _store.Reset();
        _store.Setup(s => s.Load()).Returns(new AppConfig());
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns((string?)null);
        return new MainViewModel(_store.Object, new DropParserService(), _icon.Object);
    }

    [Fact]
    public void AddBox_IncreasesCollection()
    {
        var vm = NewVm();
        vm.AddBoxCommand.Execute(null);
        vm.Boxes.Should().HaveCount(1);
        vm.Boxes[0].Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RemoveBox_DecreasesCollection()
    {
        var vm = NewVm();
        vm.AddBoxCommand.Execute(null);
        vm.RemoveBoxCommand.Execute(vm.Boxes[0]);
        vm.Boxes.Should().BeEmpty();
    }

    [Fact]
    public void Load_ReadsFromStore()
    {
        _store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Boxes = new() { new Box { Name = "已存在" } }
        });
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object);
        vm.LoadCommand.Execute(null);
        vm.Boxes.Should().ContainSingle(b => b.Name == "已存在");
    }

    [Fact]
    public void AddItemToBox_Path_AddsItem()
    {
        _store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Boxes = new() { new Box { Name = "B" } }
        });
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns("/icons/x.png");
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object);
        vm.LoadCommand.Execute(null);

        var exe = System.IO.Path.ChangeExtension(System.IO.Path.GetTempFileName(), ".exe");
        System.IO.File.WriteAllText(exe, "X");
        try
        {
            vm.AddItemToBox(vm.Boxes.First(), exe);
            vm.Boxes.First().Items.Should().ContainSingle();
            vm.Boxes.First().Items[0].IconCachePath.Should().Be("/icons/x.png");
        }
        finally { System.IO.File.Delete(exe); }
    }

    [Fact]
    public void Save_PersistsCurrentBoxes()
    {
        var vm = NewVm();
        vm.AddBoxCommand.Execute(null);
        _store.Invocations.Clear(); // 排除防抖期间的调用
        vm.Save();                   // 显式落盘
        _store.Verify(s => s.Save(It.Is<AppConfig>(c => c.Boxes.Count == 1)), Times.Once);
    }
}
