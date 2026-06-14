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
    private readonly Mock<IOrganizeService> _organize = new();

    private MainViewModel NewVm()
    {
        _store.Reset();
        _store.Setup(s => s.Load()).Returns(new AppConfig());
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns((string?)null);
        _organize.SetupGet(o => o.HasActiveOrganize).Returns(false);
        _organize.Setup(o => o.CountOrganizable()).Returns(0);
        return new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object);
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
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object);
        vm.LoadCommand.Execute(null);
        vm.Boxes.Should().ContainSingle(b => b.Name == "已存在");
    }

    [Fact]
    public void AddItemToBox_Path_AddsItemWithCorrectPath()
    {
        // 图标提取已改为后台异步,本测试只断言条目被加入且路径正确(不依赖异步时序)
        _store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Boxes = new() { new Box { Name = "B" } }
        });
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns("/icons/x.png");
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object);
        vm.LoadCommand.Execute(null);

        var exe = System.IO.Path.ChangeExtension(System.IO.Path.GetTempFileName(), ".exe");
        System.IO.File.WriteAllText(exe, "X");
        try
        {
            vm.AddItemToBox(vm.Boxes.First(), exe);
            vm.Boxes.First().Items.Should().ContainSingle();
            vm.Boxes.First().Items[0].TargetPath.Should().Be(exe);
            vm.Boxes.First().Items[0].Type.Should().Be(ItemType.File);
        }
        finally { System.IO.File.Delete(exe); }
    }

    [Fact]
    public void Save_PersistsCurrentBoxes()
    {
        var vm = NewVm();
        vm.AddBoxCommand.Execute(null);
        _store.Invocations.Clear();
        vm.Save();
        _store.Verify(s => s.Save(It.Is<AppConfig>(c => c.Boxes.Count == 1)), Times.Once);
    }
}
