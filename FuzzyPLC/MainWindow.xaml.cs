using System.Windows;
using System.Windows.Controls;
using FuzzyPLC.Models;
using FuzzyPLC.ViewModels;

namespace FuzzyPLC
{
    public partial class MainWindow : Window
    {
        MainViewModel VM => (MainViewModel)DataContext;
        public MainWindow() { InitializeComponent(); }

        void BtnExample_Click    (object s, RoutedEventArgs e) => VM.LoadExample();
        void BtnLoad_Click       (object s, RoutedEventArgs e) => VM.LoadProject();
        void BtnSave_Click       (object s, RoutedEventArgs e) => VM.SaveProject();
        void BtnAddVar_Click     (object s, RoutedEventArgs e) => VM.AddVariable();
        void BtnCalculate_Click  (object s, RoutedEventArgs e) => VM.Calculate();
        void BtnRemoveVar_Click  (object s, RoutedEventArgs e) { if (VM.SelectedVar != null) VM.RemoveVariable(VM.SelectedVar); }
        void BtnSetMain_Click    (object s, RoutedEventArgs e) { if (VM.SelectedVar != null) VM.SetAsMain(VM.SelectedVar); }
        void BtnExportExcel_Click(object s, RoutedEventArgs e) => VM.ExportExcel();
        void BtnExportCsv_Click  (object s, RoutedEventArgs e) => VM.ExportCsv();
        void BtnSaveAs_Click     (object s, RoutedEventArgs e) => VM.SaveProjectAs();
        void ChkIsMain_Click     (object s, RoutedEventArgs e)
        {
            if (s is CheckBox chk && chk.IsChecked == true && VM.SelectedVar != null)
                VM.SetAsMain(VM.SelectedVar);
        }
    }
}
