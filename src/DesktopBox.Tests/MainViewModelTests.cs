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
    private readonly Mock<IDesktopIconsService> _desktopIcons = new();
    private readonly Mock<ILocalizerService> _localizer = new();
    private readonly Mock<IShellChangeNotifierService> _shellChange = new();

    private MainViewModel NewVm()
    {
        _store.Reset();
        // Localizer 索引器回退:返回 key 本身(模拟"无翻译"行为)
        _localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => k);
        _store.Setup(s => s.Load()).Returns(new AppConfig());
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns((string?)null);
        _organize.SetupGet(o => o.HasActiveOrganize).Returns(false);
        _organize.Setup(o => o.CountOrganizable()).Returns(0);
        _desktopIcons.SetupGet(d => d.AreIconsVisible).Returns(true);
        return new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object, _desktopIcons.Object, _localizer.Object, _shellChange.Object);
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
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object, _desktopIcons.Object, _localizer.Object, _shellChange.Object);
        vm.LoadCommand.Execute(null);
        vm.Boxes.Should().ContainSingle(b => b.Name == "已存在");
    }

    [Fact]
    public void AddItemToBox_Path_AddsItemWithCorrectPath()
    {
        _store.Setup(s => s.Load()).Returns(new AppConfig
        {
            Boxes = new() { new Box { Name = "B" } }
        });
        _icon.Setup(i => i.Extract(It.IsAny<string>())).Returns("/icons/x.png");
        var vm = new MainViewModel(_store.Object, new DropParserService(), _icon.Object, _organize.Object, _desktopIcons.Object, _localizer.Object, _shellChange.Object);
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

    // ToggleDesktopIcons 依赖 GUI 对话框(InputDialog.Inform),命令末尾会弹窗,
    // 无法在无界面测试中执行。其逻辑仅为"翻转并调用 SetVisible",靠桌面图标服务的
    // 手动验证覆盖。
}
