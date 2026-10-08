import sys
from PyQt5.QtWidgets import QMainWindow, QApplication
from mywindow import Ui_MainWindow


class MyDesiger(QMainWindow, Ui_MainWindow):
    def __init__(self, parent=None):
        super(MyDesiger, self).__init__(parent)
        self.setupUi(self)

    def mousePressEvent(self, event):
        return Ui_MainWindow.mousePressEvent(self, event)

    def mouseMoveEvent(self, event):
        return Ui_MainWindow.mouseMoveEvent(self, event)

    def mouseReleaseEvent(self, event):
        return Ui_MainWindow.mouseReleaseEvent(self, event)

if __name__ == "__main__":
    app = QApplication(sys.argv)
    ui = MyDesiger()
    ui.show()
    sys.exit(app.exec_())
