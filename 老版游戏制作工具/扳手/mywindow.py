from asyncio.windows_events import NULL
from ctypes.wintypes import HKEY
from pyexpat import version_info
import time
from PyQt5.QtGui import QIcon
from PyQt5 import QtCore, QtGui, QtWidgets
import win32api
import win32gui
import win32con
import win32process
import psutil
import ctypes
import os
import sys
from pathlib import Path
from hook import HookError, my_hook
from engine_profile import (EFFECT_JOB_COUNT_66,
                            EngineVersion,
                            EffectAssignmentRow8,
                            EffectAssignmentRow66, EffectAssignmentSlot66,
                            PEImage, ProfileError, RuntimeModuleImage,
                            detect_composite_profile,
                            encode_effect_assignment_row_8,
                            encode_effect_assignment_row_66, legacy_profile,
                            parse_effect_assignment_row_8,
                            parse_effect_assignment_row_66,
                            treasure_ids_66, verify_engine_identity)
from engine_layout import (AddressResolverKind, CompareWriteEntry,
                           FeatureAccess, RawRecordSnapshot,
                           ResolvedTableDescriptor, TableLayoutSpec)
from process_memory import MemoryAccessError, MemorySession, PatchError, get_process_module
import csv

version = 1 # -1是6.4状态修正版，0..6为稳定引擎版本标识
#-1 是圣三
len_war = 0x24
addr_war = 0x4B2C50
cnt_wo = 15
cnt_you = 20
cnt_di = 80
cnt_item = 154
addr_tianfu = 0x5089B0
addr_zhuanshu = 0x50E800
addr_bisha = 0x511800
life = b'\xE0\x92\x40\x00'
condition_change = False  #star在6.4修正版中 修改了状态的内存约定

lock_list = []
lock_hp = []
lock_mp = []
lock_ids = []
auto_life = False

class myTextEdit(QtWidgets.QTextEdit):
    def __init__(self,parent):
        super(myTextEdit, self).__init__(parent)
        #绑定textchanged事件
        self.textChanged.connect(self.handleTextChanged)

    def handleTextChanged(self):
        tmp = self.toPlainText()
        res = ''.join(filter(str.isdigit, tmp))
        if res != tmp:
            self.setPlainText(res)

class Ui_MainWindow(object):
    def setupUi(self, MainWindow):
        MainWindow.setObjectName("MainWindow")
        MainWindow.resize(700, 530)
        MainWindow.setFixedSize(MainWindow.width(), MainWindow.height())
        self.font = QtGui.QFont()
        self.font.setFamily("宋体")
        self.font.setPointSize(18)
        self.centralwidget = QtWidgets.QWidget(MainWindow)
        self.centralwidget.setObjectName("centralwidget")
        self.frame = QtWidgets.QFrame(self.centralwidget)
        self.frame.setGeometry(QtCore.QRect(9, 77, 681, 389))
        self.frame.setFrameShape(QtWidgets.QFrame.Box)
        self.frame.setFrameShadow(QtWidgets.QFrame.Raised)
        self.frame.setObjectName("frame")
        self.widget_1 = QtWidgets.QWidget(self.frame)
        self.widget_1.setEnabled(True)
        self.widget_1.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_1.setObjectName("widget_1")
        self.checkBox_1 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_1.setGeometry(QtCore.QRect(20, 38, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_1.setFont(font)
        self.checkBox_1.setAutoFillBackground(False)
        self.checkBox_1.setObjectName("checkBox_1")
        self.checkBox_2 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_2.setGeometry(QtCore.QRect(20, 74, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_2.setFont(font)
        self.checkBox_2.setAutoFillBackground(False)
        self.checkBox_2.setObjectName("checkBox_2")
        self.checkBox_3 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_3.setGeometry(QtCore.QRect(20, 110, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_3.setFont(font)
        self.checkBox_3.setAutoFillBackground(False)
        self.checkBox_3.setObjectName("checkBox_3")
        self.checkBox_4 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_4.setGeometry(QtCore.QRect(20, 146, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_4.setFont(font)
        self.checkBox_4.setAutoFillBackground(False)
        self.checkBox_4.setObjectName("checkBox_4")
        self.checkBox_5 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_5.setGeometry(QtCore.QRect(20, 182, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_5.setFont(font)
        self.checkBox_5.setAutoFillBackground(False)
        self.checkBox_5.setObjectName("checkBox_5")
        self.checkBox_6 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_6.setGeometry(QtCore.QRect(20, 218, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_6.setFont(font)
        self.checkBox_6.setAutoFillBackground(False)
        self.checkBox_6.setObjectName("checkBox_6")
        self.checkBox_7 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_7.setGeometry(QtCore.QRect(20, 254, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_7.setFont(font)
        self.checkBox_7.setAutoFillBackground(False)
        self.checkBox_7.setObjectName("checkBox_7")
        self.checkBox_8 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_8.setGeometry(QtCore.QRect(20, 290, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_8.setFont(font)
        self.checkBox_8.setAutoFillBackground(False)
        self.checkBox_8.setObjectName("checkBox_8")
        self.checkBox_9 = QtWidgets.QCheckBox(self.widget_1)
        self.checkBox_9.setGeometry(QtCore.QRect(20, 326, 171, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        self.checkBox_9.setFont(font)
        self.checkBox_9.setAutoFillBackground(False)
        self.checkBox_9.setObjectName("checkBox_9")
        self.widget_2 = QtWidgets.QWidget(self.frame)
        self.widget_2.setEnabled(True)
        self.widget_2.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_2.setObjectName("widget_2")
        self.listview_data = QtWidgets.QListWidget(self.widget_2)
        self.listview_data.setGeometry(QtCore.QRect(10, 13, 140, 365))
        font = QtGui.QFont()
        font.setPointSize(9)
        self.listview_data.setFont(font)
        self.listview_data.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.listview_data.setFrameShape(QtWidgets.QFrame.Panel)
        self.listview_data.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.listview_data.setLineWidth(2)
        self.listview_data.setAutoScroll(True)
        self.listview_data.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        self.listview_data.setDefaultDropAction(QtCore.Qt.MoveAction)
        self.listview_data.setObjectName("listview_data")
        self.data_1 = QtWidgets.QLabel(self.widget_2)
        self.data_1.setGeometry(QtCore.QRect(165, 13, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_1.setFont(font)
        self.data_1.setStyleSheet("color:rgb(0,0,0);")
        self.data_1.setObjectName("data_1")
        self.data_input_1 = myTextEdit(self.widget_2)
        self.data_input_1.setGeometry(QtCore.QRect(230, 12, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_1.setFont(font)
        self.data_input_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_1.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_1.setLineWidth(2)
        self.data_input_1.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_1.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_1.setObjectName("data_input_1")
        self.data_2 = QtWidgets.QLabel(self.widget_2)
        self.data_2.setGeometry(QtCore.QRect(165, 48, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_2.setFont(font)
        self.data_2.setStyleSheet("color:rgb(0,0,0);")
        self.data_2.setObjectName("data_2")
        self.data_input_2 = myTextEdit(self.widget_2)
        self.data_input_2.setGeometry(QtCore.QRect(230, 47, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_2.setFont(font)
        self.data_input_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_2.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_2.setLineWidth(2)
        self.data_input_2.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_2.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_2.setObjectName("data_input_2")
        self.data_input_3 = myTextEdit(self.widget_2)
        self.data_input_3.setGeometry(QtCore.QRect(230, 82, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_3.setFont(font)
        self.data_input_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_3.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_3.setLineWidth(2)
        self.data_input_3.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_3.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_3.setObjectName("data_input_3")
        self.data_3 = QtWidgets.QLabel(self.widget_2)
        self.data_3.setGeometry(QtCore.QRect(165, 83, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_3.setFont(font)
        self.data_3.setStyleSheet("color:rgb(0,0,0);")
        self.data_3.setObjectName("data_3")
        self.data_input_4 = myTextEdit(self.widget_2)
        self.data_input_4.setGeometry(QtCore.QRect(230, 117, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_4.setFont(font)
        self.data_input_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_4.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_4.setLineWidth(2)
        self.data_input_4.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_4.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_4.setObjectName("data_input_4")
        self.data_4 = QtWidgets.QLabel(self.widget_2)
        self.data_4.setGeometry(QtCore.QRect(165, 118, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_4.setFont(font)
        self.data_4.setStyleSheet("color:rgb(0,0,0);")
        self.data_4.setObjectName("data_4")
        self.data_5 = QtWidgets.QLabel(self.widget_2)
        self.data_5.setGeometry(QtCore.QRect(165, 153, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_5.setFont(font)
        self.data_5.setStyleSheet("color:rgb(0,0,0);")
        self.data_5.setObjectName("data_5")
        self.data_input_5 = myTextEdit(self.widget_2)
        self.data_input_5.setGeometry(QtCore.QRect(230, 152, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_5.setFont(font)
        self.data_input_5.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_5.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_5.setLineWidth(2)
        self.data_input_5.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_5.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_5.setObjectName("data_input_5")
        self.data_6 = QtWidgets.QLabel(self.widget_2)
        self.data_6.setGeometry(QtCore.QRect(165, 188, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_6.setFont(font)
        self.data_6.setStyleSheet("color:rgb(0,0,0);")
        self.data_6.setObjectName("data_6")
        self.data_input_6 = myTextEdit(self.widget_2)
        self.data_input_6.setGeometry(QtCore.QRect(230, 187, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_6.setFont(font)
        self.data_input_6.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_6.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_6.setLineWidth(2)
        self.data_input_6.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_6.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_6.setObjectName("data_input_6")
        self.data_input_7 = myTextEdit(self.widget_2)
        self.data_input_7.setGeometry(QtCore.QRect(230, 222, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_7.setFont(font)
        self.data_input_7.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_7.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_7.setLineWidth(2)
        self.data_input_7.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_7.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_7.setObjectName("data_input_7")
        self.data_7 = QtWidgets.QLabel(self.widget_2)
        self.data_7.setGeometry(QtCore.QRect(165, 223, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_7.setFont(font)
        self.data_7.setStyleSheet("color:rgb(0,0,0);")
        self.data_7.setObjectName("data_7")
        self.data_input_8 = myTextEdit(self.widget_2)
        self.data_input_8.setGeometry(QtCore.QRect(230, 257, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_8.setFont(font)
        self.data_input_8.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_8.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_8.setLineWidth(2)
        self.data_input_8.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_8.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_8.setObjectName("data_input_8")
        self.data_8 = QtWidgets.QLabel(self.widget_2)
        self.data_8.setGeometry(QtCore.QRect(165, 258, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_8.setFont(font)
        self.data_8.setStyleSheet("color:rgb(0,0,0);")
        self.data_8.setObjectName("data_8")
        self.data_input_9 = myTextEdit(self.widget_2)
        self.data_input_9.setGeometry(QtCore.QRect(230, 292, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_9.setFont(font)
        self.data_input_9.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_9.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_9.setLineWidth(2)
        self.data_input_9.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_9.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_9.setObjectName("data_input_9")
        self.data_9 = QtWidgets.QLabel(self.widget_2)
        self.data_9.setGeometry(QtCore.QRect(165, 293, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_9.setFont(font)
        self.data_9.setStyleSheet("color:rgb(0,0,0);")
        self.data_9.setObjectName("data_9")
        self.data_input_10 = myTextEdit(self.widget_2)
        self.data_input_10.setGeometry(QtCore.QRect(230, 327, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_10.setFont(font)
        self.data_input_10.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_10.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_10.setLineWidth(2)
        self.data_input_10.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_10.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_10.setObjectName("data_input_10")
        self.data_10 = QtWidgets.QLabel(self.widget_2)
        self.data_10.setGeometry(QtCore.QRect(165, 328, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_10.setFont(font)
        self.data_10.setStyleSheet("color:rgb(0,0,0);")
        self.data_10.setObjectName("data_10")
        self.data_11 = QtWidgets.QLabel(self.widget_2)
        self.data_11.setGeometry(QtCore.QRect(270, 13, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_11.setFont(font)
        self.data_11.setStyleSheet("color:rgb(0,0,0);")
        self.data_11.setObjectName("data_11")
        self.data_input_11 = QtWidgets.QCheckBox(self.widget_2)
        self.data_input_11.setGeometry(QtCore.QRect(336, 14, 91, 19))
        self.data_input_11.setText("")
        self.data_input_11.setChecked(False)
        self.data_input_11.setObjectName("data_input_11")
        self.data_input_12 = myTextEdit(self.widget_2)
        self.data_input_12.setGeometry(QtCore.QRect(335, 47, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_12.setFont(font)
        self.data_input_12.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_12.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_12.setLineWidth(2)
        self.data_input_12.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_12.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_12.setObjectName("data_input_12")
        self.data_12 = QtWidgets.QLabel(self.widget_2)
        self.data_12.setGeometry(QtCore.QRect(270, 48, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_12.setFont(font)
        self.data_12.setStyleSheet("color:rgb(0,0,0);")
        self.data_12.setObjectName("data_12")
        self.data_input_13 = myTextEdit(self.widget_2)
        self.data_input_13.setGeometry(QtCore.QRect(335, 82, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_13.setFont(font)
        self.data_input_13.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_13.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_13.setLineWidth(2)
        self.data_input_13.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_13.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_13.setObjectName("data_input_13")
        self.data_13 = QtWidgets.QLabel(self.widget_2)
        self.data_13.setGeometry(QtCore.QRect(270, 83, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_13.setFont(font)
        self.data_13.setStyleSheet("color:rgb(0,0,0);")
        self.data_13.setObjectName("data_13")
        self.data_input_14 = myTextEdit(self.widget_2)
        self.data_input_14.setGeometry(QtCore.QRect(335, 117, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_14.setFont(font)
        self.data_input_14.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_14.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_14.setLineWidth(2)
        self.data_input_14.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_14.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_14.setObjectName("data_input_14")
        self.data_14 = QtWidgets.QLabel(self.widget_2)
        self.data_14.setGeometry(QtCore.QRect(270, 118, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_14.setFont(font)
        self.data_14.setStyleSheet("color:rgb(0,0,0);")
        self.data_14.setObjectName("data_14")
        self.data_15 = QtWidgets.QLabel(self.widget_2)
        self.data_15.setGeometry(QtCore.QRect(270, 153, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_15.setFont(font)
        self.data_15.setStyleSheet("color:rgb(0,0,0);")
        self.data_15.setObjectName("data_15")
        self.data_input_15 = myTextEdit(self.widget_2)
        self.data_input_15.setGeometry(QtCore.QRect(335, 152, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_15.setFont(font)
        self.data_input_15.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_15.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_15.setLineWidth(2)
        self.data_input_15.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_15.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_15.setObjectName("data_input_15")
        self.data_16 = QtWidgets.QLabel(self.widget_2)
        self.data_16.setGeometry(QtCore.QRect(270, 188, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_16.setFont(font)
        self.data_16.setStyleSheet("color:rgb(0,0,0);")
        self.data_16.setObjectName("data_16")
        self.data_input_16 = myTextEdit(self.widget_2)
        self.data_input_16.setGeometry(QtCore.QRect(335, 187, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_16.setFont(font)
        self.data_input_16.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_16.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_16.setLineWidth(2)
        self.data_input_16.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_16.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_16.setObjectName("data_input_16")
        self.data_input_17 = myTextEdit(self.widget_2)
        self.data_input_17.setGeometry(QtCore.QRect(335, 222, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_17.setFont(font)
        self.data_input_17.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_17.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_17.setLineWidth(2)
        self.data_input_17.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_17.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_17.setObjectName("data_input_17")
        self.data_17 = QtWidgets.QLabel(self.widget_2)
        self.data_17.setGeometry(QtCore.QRect(270, 223, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_17.setFont(font)
        self.data_17.setStyleSheet("color:rgb(0,0,0);")
        self.data_17.setObjectName("data_17")
        self.data_18 = QtWidgets.QLabel(self.widget_2)
        self.data_18.setGeometry(QtCore.QRect(270, 258, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_18.setFont(font)
        self.data_18.setStyleSheet("color:rgb(0,0,0);")
        self.data_18.setObjectName("data_18")
        self.data_input_18 = myTextEdit(self.widget_2)
        self.data_input_18.setGeometry(QtCore.QRect(335, 257, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_18.setFont(font)
        self.data_input_18.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_18.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_18.setLineWidth(2)
        self.data_input_18.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_18.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_18.setObjectName("data_input_18")
        self.data_19 = QtWidgets.QLabel(self.widget_2)
        self.data_19.setGeometry(QtCore.QRect(270, 293, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_19.setFont(font)
        self.data_19.setStyleSheet("color:rgb(0,0,0);")
        self.data_19.setObjectName("data_19")
        self.data_input_19 = myTextEdit(self.widget_2)
        self.data_input_19.setGeometry(QtCore.QRect(335, 292, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_19.setFont(font)
        self.data_input_19.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_19.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_19.setLineWidth(2)
        self.data_input_19.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_19.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_19.setObjectName("data_input_19")
        self.data_20 = QtWidgets.QLabel(self.widget_2)
        self.data_20.setGeometry(QtCore.QRect(270, 328, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_20.setFont(font)
        self.data_20.setStyleSheet("color:rgb(0,0,0);")
        self.data_20.setObjectName("data_20")
        self.data_input_20 = myTextEdit(self.widget_2)
        self.data_input_20.setGeometry(QtCore.QRect(335, 327, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_20.setFont(font)
        self.data_input_20.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_20.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_20.setLineWidth(2)
        self.data_input_20.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_20.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_20.setObjectName("data_input_20")
        self.data_21 = QtWidgets.QLabel(self.widget_2)
        self.data_21.setGeometry(QtCore.QRect(375, 13, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_21.setFont(font)
        self.data_21.setStyleSheet("color:rgb(0,0,0);")
        self.data_21.setObjectName("data_21")
        self.data_input_21 = myTextEdit(self.widget_2)
        self.data_input_21.setGeometry(QtCore.QRect(440, 12, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_21.setFont(font)
        self.data_input_21.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_21.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_21.setLineWidth(2)
        self.data_input_21.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_21.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_21.setObjectName("data_input_21")
        self.data_input_22 = myTextEdit(self.widget_2)
        self.data_input_22.setGeometry(QtCore.QRect(440, 47, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_22.setFont(font)
        self.data_input_22.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_22.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_22.setLineWidth(2)
        self.data_input_22.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_22.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_22.setObjectName("data_input_22")
        self.data_22 = QtWidgets.QLabel(self.widget_2)
        self.data_22.setGeometry(QtCore.QRect(375, 48, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_22.setFont(font)
        self.data_22.setStyleSheet("color:rgb(0,0,0);")
        self.data_22.setObjectName("data_22")
        self.data_23 = QtWidgets.QLabel(self.widget_2)
        self.data_23.setGeometry(QtCore.QRect(375, 83, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_23.setFont(font)
        self.data_23.setStyleSheet("color:rgb(0,0,0);")
        self.data_23.setObjectName("data_23")
        self.data_input_23 = myTextEdit(self.widget_2)
        self.data_input_23.setGeometry(QtCore.QRect(440, 82, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_23.setFont(font)
        self.data_input_23.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_23.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_23.setLineWidth(2)
        self.data_input_23.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_23.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_23.setObjectName("data_input_23")
        self.data_input_24 = myTextEdit(self.widget_2)
        self.data_input_24.setGeometry(QtCore.QRect(440, 117, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_24.setFont(font)
        self.data_input_24.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_24.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_24.setLineWidth(2)
        self.data_input_24.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_24.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_24.setObjectName("data_input_24")
        self.data_24 = QtWidgets.QLabel(self.widget_2)
        self.data_24.setGeometry(QtCore.QRect(375, 118, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_24.setFont(font)
        self.data_24.setStyleSheet("color:rgb(0,0,0);")
        self.data_24.setObjectName("data_24")
        self.data_input_25 = myTextEdit(self.widget_2)
        self.data_input_25.setGeometry(QtCore.QRect(440, 152, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_25.setFont(font)
        self.data_input_25.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_25.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_25.setLineWidth(2)
        self.data_input_25.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_25.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_25.setObjectName("data_input_25")
        self.data_25 = QtWidgets.QLabel(self.widget_2)
        self.data_25.setGeometry(QtCore.QRect(375, 153, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_25.setFont(font)
        self.data_25.setStyleSheet("color:rgb(0,0,0);")
        self.data_25.setObjectName("data_25")
        self.data_26 = QtWidgets.QLabel(self.widget_2)
        self.data_26.setGeometry(QtCore.QRect(375, 188, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_26.setFont(font)
        self.data_26.setStyleSheet("color:rgb(0,0,0);")
        self.data_26.setObjectName("data_26")
        self.data_input_26 = myTextEdit(self.widget_2)
        self.data_input_26.setGeometry(QtCore.QRect(440, 187, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_26.setFont(font)
        self.data_input_26.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_26.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_26.setLineWidth(2)
        self.data_input_26.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_26.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_26.setObjectName("data_input_26")
        self.data_input_27 = myTextEdit(self.widget_2)
        self.data_input_27.setGeometry(QtCore.QRect(440, 222, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_27.setFont(font)
        self.data_input_27.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_27.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_27.setLineWidth(2)
        self.data_input_27.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_27.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_27.setObjectName("data_input_27")
        self.data_27 = QtWidgets.QLabel(self.widget_2)
        self.data_27.setGeometry(QtCore.QRect(375, 223, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_27.setFont(font)
        self.data_27.setStyleSheet("color:rgb(0,0,0);")
        self.data_27.setObjectName("data_27")
        self.data_28 = QtWidgets.QLabel(self.widget_2)
        self.data_28.setGeometry(QtCore.QRect(375, 258, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_28.setFont(font)
        self.data_28.setStyleSheet("color:rgb(0,0,0);")
        self.data_28.setObjectName("data_28")
        self.data_input_28 = myTextEdit(self.widget_2)
        self.data_input_28.setGeometry(QtCore.QRect(440, 257, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_28.setFont(font)
        self.data_input_28.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_28.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_28.setLineWidth(2)
        self.data_input_28.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_28.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_28.setObjectName("data_input_28")
        self.data_29 = QtWidgets.QLabel(self.widget_2)
        self.data_29.setGeometry(QtCore.QRect(375, 293, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_29.setFont(font)
        self.data_29.setStyleSheet("color:rgb(0,0,0);")
        self.data_29.setObjectName("data_29")
        self.data_input_29 = myTextEdit(self.widget_2)
        self.data_input_29.setGeometry(QtCore.QRect(440, 292, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_29.setFont(font)
        self.data_input_29.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_29.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_29.setLineWidth(2)
        self.data_input_29.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_29.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_29.setObjectName("data_input_29")
        self.data_input_30 = myTextEdit(self.widget_2)
        self.data_input_30.setGeometry(QtCore.QRect(440, 327, 50, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_30.setFont(font)
        self.data_input_30.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_30.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_30.setLineWidth(2)
        self.data_input_30.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_30.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_30.setObjectName("data_input_30")
        self.data_30 = QtWidgets.QLabel(self.widget_2)
        self.data_30.setGeometry(QtCore.QRect(375, 328, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_30.setFont(font)
        self.data_30.setStyleSheet("color:rgb(0,0,0);")
        self.data_30.setObjectName("data_30")
        self.data_31 = QtWidgets.QLabel(self.widget_2)
        self.data_31.setGeometry(QtCore.QRect(480, 13, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_31.setFont(font)
        self.data_31.setStyleSheet("color:rgb(0,0,0);")
        self.data_31.setObjectName("data_31")
        self.data_input_31 = QtWidgets.QComboBox(self.widget_2)
        self.data_input_31.setGeometry(QtCore.QRect(545, 12, 131, 25))
        self.data_input_31.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_31.setObjectName("data_input_31")
        self.data_32 = QtWidgets.QLabel(self.widget_2)
        self.data_32.setGeometry(QtCore.QRect(480, 48, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_32.setFont(font)
        self.data_32.setStyleSheet("color:rgb(0,0,0);")
        self.data_32.setObjectName("data_32")
        self.data_input_32 = QtWidgets.QComboBox(self.widget_2)
        self.data_input_32.setGeometry(QtCore.QRect(545, 47, 131, 25))
        self.data_input_32.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_32.setEditable(False)
        self.data_input_32.setObjectName("data_input_32")
        self.data_input_33 = myTextEdit(self.widget_2)
        self.data_input_33.setGeometry(QtCore.QRect(545, 82, 121, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_33.setFont(font)
        self.data_input_33.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_33.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_33.setLineWidth(2)
        self.data_input_33.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_33.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_33.setObjectName("data_input_33")
        self.data_33 = QtWidgets.QLabel(self.widget_2)
        self.data_33.setGeometry(QtCore.QRect(480, 83, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_33.setFont(font)
        self.data_33.setStyleSheet("color:rgb(0,0,0);")
        self.data_33.setObjectName("data_33")
        self.data_34 = QtWidgets.QLabel(self.widget_2)
        self.data_34.setGeometry(QtCore.QRect(480, 118, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_34.setFont(font)
        self.data_34.setStyleSheet("color:rgb(0,0,0);")
        self.data_34.setObjectName("data_34")
        self.data_input_34 = myTextEdit(self.widget_2)
        self.data_input_34.setGeometry(QtCore.QRect(545, 117, 121, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_34.setFont(font)
        self.data_input_34.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_34.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_34.setLineWidth(2)
        self.data_input_34.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_34.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_34.setObjectName("data_input_34")
        self.data_35 = QtWidgets.QLabel(self.widget_2)
        self.data_35.setGeometry(QtCore.QRect(480, 153, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_35.setFont(font)
        self.data_35.setStyleSheet("color:rgb(0,0,0);")
        self.data_35.setObjectName("data_35")
        self.data_input_35 = QtWidgets.QComboBox(self.widget_2)
        self.data_input_35.setGeometry(QtCore.QRect(545, 152, 131, 25))
        self.data_input_35.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_35.setEditable(False)
        self.data_input_35.setObjectName("data_input_35")
        self.data_input_36 = myTextEdit(self.widget_2)
        self.data_input_36.setGeometry(QtCore.QRect(545, 187, 121, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_36.setFont(font)
        self.data_input_36.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_36.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_36.setLineWidth(2)
        self.data_input_36.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_36.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_36.setObjectName("data_input_36")
        self.data_36 = QtWidgets.QLabel(self.widget_2)
        self.data_36.setGeometry(QtCore.QRect(480, 188, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_36.setFont(font)
        self.data_36.setStyleSheet("color:rgb(0,0,0);")
        self.data_36.setObjectName("data_36")
        self.data_input_37 = myTextEdit(self.widget_2)
        self.data_input_37.setGeometry(QtCore.QRect(545, 222, 121, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.data_input_37.setFont(font)
        self.data_input_37.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_37.setFrameShape(QtWidgets.QFrame.Panel)
        self.data_input_37.setLineWidth(2)
        self.data_input_37.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_37.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.data_input_37.setObjectName("data_input_37")
        self.data_37 = QtWidgets.QLabel(self.widget_2)
        self.data_37.setGeometry(QtCore.QRect(480, 223, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_37.setFont(font)
        self.data_37.setStyleSheet("color:rgb(0,0,0);")
        self.data_37.setObjectName("data_37")
        self.data_38 = QtWidgets.QLabel(self.widget_2)
        self.data_38.setGeometry(QtCore.QRect(480, 258, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.data_38.setFont(font)
        self.data_38.setStyleSheet("color:rgb(0,0,0);")
        self.data_38.setObjectName("data_38")
        self.data_input_38 = QtWidgets.QComboBox(self.widget_2)
        self.data_input_38.setGeometry(QtCore.QRect(545, 257, 131, 25))
        self.data_input_38.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_input_38.setEditable(False)
        self.data_input_38.setObjectName("data_input_38")
        self.data_save = QtWidgets.QPushButton(self.widget_2)
        self.data_save.setGeometry(QtCore.QRect(515, 324, 151, 31))
        self.data_save.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_save.setObjectName("data_save")
        self.data_recal = QtWidgets.QPushButton(self.widget_2)
        self.data_recal.setGeometry(QtCore.QRect(515, 290, 151, 31))
        self.data_recal.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.data_recal.setObjectName("data_recal")
        self.widget_3 = QtWidgets.QWidget(self.frame)
        self.widget_3.setEnabled(True)
        self.widget_3.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_3.setObjectName("widget_3")
        self.listview_war = QtWidgets.QListWidget(self.widget_3)
        self.listview_war.setGeometry(QtCore.QRect(10, 45, 135, 301))
        font = QtGui.QFont()
        font.setPointSize(9)
        self.listview_war.setFont(font)
        self.listview_war.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.listview_war.setFrameShape(QtWidgets.QFrame.Panel)
        self.listview_war.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.listview_war.setLineWidth(2)
        self.listview_war.setAutoScroll(True)
        self.listview_war.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        self.listview_war.setDefaultDropAction(QtCore.Qt.MoveAction)
        self.listview_war.setObjectName("listview_war")
        self.war_input_2 = myTextEdit(self.widget_3)
        self.war_input_2.setGeometry(QtCore.QRect(190, 78, 90, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_2.setFont(font)
        self.war_input_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_2.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_2.setLineWidth(2)
        self.war_input_2.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_2.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_2.setObjectName("war_input_2")
        self.war_2 = QtWidgets.QLabel(self.widget_3)
        self.war_2.setGeometry(QtCore.QRect(150, 80, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_2.setFont(font)
        self.war_2.setStyleSheet("color:rgb(0,0,0);")
        self.war_2.setObjectName("war_2")
        self.war_save = QtWidgets.QPushButton(self.widget_3)
        self.war_save.setGeometry(QtCore.QRect(550, 340, 121, 35))
        self.war_save.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_save.setObjectName("war_save")
        self.war_kind_1 = QtWidgets.QRadioButton(self.widget_3)
        self.war_kind_1.setGeometry(QtCore.QRect(10, 20, 41, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_kind_1.setFont(font)
        self.war_kind_1.setObjectName("war_kind_1")
        self.war_kind_2 = QtWidgets.QRadioButton(self.widget_3)
        self.war_kind_2.setGeometry(QtCore.QRect(55, 20, 41, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_kind_2.setFont(font)
        self.war_kind_2.setObjectName("war_kind_2")
        self.war_kind_3 = QtWidgets.QRadioButton(self.widget_3)
        self.war_kind_3.setGeometry(QtCore.QRect(100, 20, 41, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_kind_3.setFont(font)
        self.war_kind_3.setObjectName("war_kind_3")
        self.war_connect = QtWidgets.QPushButton(self.widget_3)
        self.war_connect.setGeometry(QtCore.QRect(10, 355, 135, 25))
        font = QtGui.QFont()
        font.setBold(False)
        font.setWeight(50)
        self.war_connect.setFont(font)
        self.war_connect.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_connect.setObjectName("war_connect")
        self.war_input_1 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_1.setGeometry(QtCore.QRect(160, 45, 121, 25))
        self.war_input_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_1.setEditable(False)
        self.war_input_1.setObjectName("war_input_1")
        self.war_input_3 = myTextEdit(self.widget_3)
        self.war_input_3.setGeometry(QtCore.QRect(190, 108, 90, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_3.setFont(font)
        self.war_input_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_3.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_3.setLineWidth(2)
        self.war_input_3.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_3.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_3.setObjectName("war_input_3")
        self.war_3 = QtWidgets.QLabel(self.widget_3)
        self.war_3.setGeometry(QtCore.QRect(150, 110, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_3.setFont(font)
        self.war_3.setStyleSheet("color:rgb(0,0,0);")
        self.war_3.setObjectName("war_3")
        self.war_input_4 = myTextEdit(self.widget_3)
        self.war_input_4.setGeometry(QtCore.QRect(190, 138, 90, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_4.setFont(font)
        self.war_input_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_4.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_4.setLineWidth(2)
        self.war_input_4.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_4.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_4.setObjectName("war_input_4")
        self.war_4 = QtWidgets.QLabel(self.widget_3)
        self.war_4.setGeometry(QtCore.QRect(150, 140, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_4.setFont(font)
        self.war_4.setStyleSheet("color:rgb(0,0,0);")
        self.war_4.setObjectName("war_4")
        self.war_input_5 = QtWidgets.QCheckBox(self.widget_3)
        self.war_input_5.setGeometry(QtCore.QRect(190, 171, 21, 19))
        self.war_input_5.setText("")
        self.war_input_5.setChecked(False)
        self.war_input_5.setObjectName("war_input_5")
        self.war_5 = QtWidgets.QLabel(self.widget_3)
        self.war_5.setGeometry(QtCore.QRect(150, 170, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_5.setFont(font)
        self.war_5.setStyleSheet("color:rgb(0,0,0);")
        self.war_5.setObjectName("war_5")
        self.war_6 = QtWidgets.QLabel(self.widget_3)
        self.war_6.setGeometry(QtCore.QRect(220, 169, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_6.setFont(font)
        self.war_6.setStyleSheet("color:rgb(0,0,0);")
        self.war_6.setObjectName("war_6")
        self.war_input_6 = QtWidgets.QCheckBox(self.widget_3)
        self.war_input_6.setGeometry(QtCore.QRect(260, 170, 21, 19))
        self.war_input_6.setText("")
        self.war_input_6.setChecked(False)
        self.war_input_6.setObjectName("war_input_6")
        self.war_7 = QtWidgets.QLabel(self.widget_3)
        self.war_7.setGeometry(QtCore.QRect(150, 200, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_7.setFont(font)
        self.war_7.setStyleSheet("color:rgb(0,0,0);")
        self.war_7.setObjectName("war_7")
        self.war_8 = QtWidgets.QLabel(self.widget_3)
        self.war_8.setGeometry(QtCore.QRect(220, 199, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_8.setFont(font)
        self.war_8.setStyleSheet("color:rgb(0,0,0);")
        self.war_8.setObjectName("war_8")
        self.war_input_7 = QtWidgets.QCheckBox(self.widget_3)
        self.war_input_7.setGeometry(QtCore.QRect(190, 201, 21, 19))
        self.war_input_7.setText("")
        self.war_input_7.setChecked(False)
        self.war_input_7.setObjectName("war_input_7")
        self.war_input_8 = QtWidgets.QCheckBox(self.widget_3)
        self.war_input_8.setGeometry(QtCore.QRect(260, 200, 21, 19))
        self.war_input_8.setText("")
        self.war_input_8.setChecked(False)
        self.war_input_8.setObjectName("war_input_8")
        self.war_input_9 = myTextEdit(self.widget_3)
        self.war_input_9.setGeometry(QtCore.QRect(170, 228, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_9.setFont(font)
        self.war_input_9.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_9.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_9.setLineWidth(2)
        self.war_input_9.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_9.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_9.setObjectName("war_input_9")
        self.war_9 = QtWidgets.QLabel(self.widget_3)
        self.war_9.setGeometry(QtCore.QRect(141, 230, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_9.setFont(font)
        self.war_9.setStyleSheet("color:rgb(0,0,0);")
        self.war_9.setObjectName("war_9")
        self.war_10 = QtWidgets.QLabel(self.widget_3)
        self.war_10.setGeometry(QtCore.QRect(206, 230, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_10.setFont(font)
        self.war_10.setStyleSheet("color:rgb(0,0,0);")
        self.war_10.setObjectName("war_10")
        self.war_input_10 = myTextEdit(self.widget_3)
        self.war_input_10.setGeometry(QtCore.QRect(235, 228, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_10.setFont(font)
        self.war_input_10.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_10.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_10.setLineWidth(2)
        self.war_input_10.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_10.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_10.setObjectName("war_input_10")
        self.war_11 = QtWidgets.QLabel(self.widget_3)
        self.war_11.setGeometry(QtCore.QRect(146, 260, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_11.setFont(font)
        self.war_11.setStyleSheet("color:rgb(0,0,0);")
        self.war_11.setObjectName("war_11")
        self.war_input_11 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_11.setGeometry(QtCore.QRect(170, 258, 110, 25))
        self.war_input_11.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_11.setEditable(False)
        self.war_input_11.setObjectName("war_input_11")
        self.war_input_11.addItem("")
        self.war_input_11.addItem("")
        self.war_input_11.addItem("")
        self.war_input_11.addItem("")
        self.war_input_11.addItem("")
        self.war_input_11.addItem("")
        self.war_input_11.addItem("")
        self.war_input_12 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_12.setGeometry(QtCore.QRect(185, 290, 96, 25))
        self.war_input_12.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_12.setEditable(False)
        self.war_input_12.setObjectName("war_input_12")
        self.war_12 = QtWidgets.QLabel(self.widget_3)
        self.war_12.setGeometry(QtCore.QRect(150, 292, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_12.setFont(font)
        self.war_12.setStyleSheet("color:rgb(0,0,0);")
        self.war_12.setObjectName("war_12")
        self.war_input_14 = myTextEdit(self.widget_3)
        self.war_input_14.setGeometry(QtCore.QRect(235, 323, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_14.setFont(font)
        self.war_input_14.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_14.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_14.setLineWidth(2)
        self.war_input_14.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_14.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_14.setObjectName("war_input_14")
        self.war_13 = QtWidgets.QLabel(self.widget_3)
        self.war_13.setGeometry(QtCore.QRect(145, 326, 21, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_13.setFont(font)
        self.war_13.setStyleSheet("color:rgb(0,0,0);")
        self.war_13.setObjectName("war_13")
        self.war_14 = QtWidgets.QLabel(self.widget_3)
        self.war_14.setGeometry(QtCore.QRect(210, 326, 21, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_14.setFont(font)
        self.war_14.setStyleSheet("color:rgb(0,0,0);")
        self.war_14.setObjectName("war_14")
        self.war_input_13 = myTextEdit(self.widget_3)
        self.war_input_13.setGeometry(QtCore.QRect(170, 323, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_13.setFont(font)
        self.war_input_13.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_13.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_13.setLineWidth(2)
        self.war_input_13.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_13.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_13.setObjectName("war_input_13")
        self.war_15 = QtWidgets.QLabel(self.widget_3)
        self.war_15.setGeometry(QtCore.QRect(285, 45, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_15.setFont(font)
        self.war_15.setStyleSheet("color:rgb(0,0,0);")
        self.war_15.setObjectName("war_15")
        self.war_input_15 = QtWidgets.QCheckBox(self.widget_3)
        self.war_input_15.setGeometry(QtCore.QRect(320, 46, 21, 19))
        self.war_input_15.setText("")
        self.war_input_15.setChecked(False)
        self.war_input_15.setObjectName("war_input_15")
        self.war_16 = QtWidgets.QLabel(self.widget_3)
        self.war_16.setGeometry(QtCore.QRect(285, 80, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_16.setFont(font)
        self.war_16.setStyleSheet("color:rgb(0,0,0);")
        self.war_16.setObjectName("war_16")
        self.war_input_16 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_16.setGeometry(QtCore.QRect(320, 78, 61, 25))
        self.war_input_16.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_16.setEditable(False)
        self.war_input_16.setObjectName("war_input_16")
        self.war_input_16.addItem("")
        self.war_input_16.addItem("")
        self.war_input_16.addItem("")
        self.war_input_16.addItem("")
        self.war_input_16.addItem("")
        self.war_input_17 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_17.setGeometry(QtCore.QRect(320, 108, 61, 25))
        self.war_input_17.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_17.setEditable(False)
        self.war_input_17.setObjectName("war_input_17")
        self.war_input_17.addItem("")
        self.war_input_17.addItem("")
        self.war_input_17.addItem("")
        self.war_17 = QtWidgets.QLabel(self.widget_3)
        self.war_17.setGeometry(QtCore.QRect(285, 110, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_17.setFont(font)
        self.war_17.setStyleSheet("color:rgb(0,0,0);")
        self.war_17.setObjectName("war_17")
        self.war_18 = QtWidgets.QLabel(self.widget_3)
        self.war_18.setGeometry(QtCore.QRect(285, 140, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_18.setFont(font)
        self.war_18.setStyleSheet("color:rgb(0,0,0);")
        self.war_18.setObjectName("war_18")
        self.war_input_18 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_18.setGeometry(QtCore.QRect(320, 138, 61, 25))
        self.war_input_18.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_18.setEditable(False)
        self.war_input_18.setObjectName("war_input_18")
        self.war_input_18.addItem("")
        self.war_input_18.addItem("")
        self.war_input_18.addItem("")
        self.war_input_19 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_19.setGeometry(QtCore.QRect(320, 168, 61, 25))
        self.war_input_19.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_19.setEditable(False)
        self.war_input_19.setObjectName("war_input_19")
        self.war_input_19.addItem("")
        self.war_input_19.addItem("")
        self.war_input_19.addItem("")
        self.war_19 = QtWidgets.QLabel(self.widget_3)
        self.war_19.setGeometry(QtCore.QRect(285, 170, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_19.setFont(font)
        self.war_19.setStyleSheet("color:rgb(0,0,0);")
        self.war_19.setObjectName("war_19")
        self.war_20 = QtWidgets.QLabel(self.widget_3)
        self.war_20.setGeometry(QtCore.QRect(285, 200, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_20.setFont(font)
        self.war_20.setStyleSheet("color:rgb(0,0,0);")
        self.war_20.setObjectName("war_20")
        self.war_input_20 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_20.setGeometry(QtCore.QRect(320, 198, 61, 25))
        self.war_input_20.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_20.setEditable(False)
        self.war_input_20.setObjectName("war_input_20")
        self.war_input_20.addItem("")
        self.war_input_20.addItem("")
        self.war_input_20.addItem("")
        self.war_input_21 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_21.setGeometry(QtCore.QRect(320, 228, 61, 25))
        self.war_input_21.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_21.setEditable(False)
        self.war_input_21.setObjectName("war_input_21")
        self.war_input_21.addItem("")
        self.war_input_21.addItem("")
        self.war_input_21.addItem("")
        self.war_21 = QtWidgets.QLabel(self.widget_3)
        self.war_21.setGeometry(QtCore.QRect(285, 230, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_21.setFont(font)
        self.war_21.setStyleSheet("color:rgb(0,0,0);")
        self.war_21.setObjectName("war_21")
        self.war_input_22 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_22.setGeometry(QtCore.QRect(320, 258, 61, 25))
        self.war_input_22.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_22.setEditable(False)
        self.war_input_22.setObjectName("war_input_22")
        self.war_input_22.addItem("")
        self.war_input_22.addItem("")
        self.war_input_22.addItem("")
        self.war_22 = QtWidgets.QLabel(self.widget_3)
        self.war_22.setGeometry(QtCore.QRect(285, 260, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_22.setFont(font)
        self.war_22.setStyleSheet("color:rgb(0,0,0);")
        self.war_22.setObjectName("war_22")
        self.war_input_23 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_23.setGeometry(QtCore.QRect(320, 288, 61, 25))
        self.war_input_23.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_23.setEditable(False)
        self.war_input_23.setObjectName("war_input_23")
        self.war_input_23.addItem("")
        self.war_input_23.addItem("")
        self.war_input_23.addItem("")
        self.war_input_23.addItem("")
        self.war_23 = QtWidgets.QLabel(self.widget_3)
        self.war_23.setGeometry(QtCore.QRect(285, 290, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_23.setFont(font)
        self.war_23.setStyleSheet("color:rgb(0,0,0);")
        self.war_23.setObjectName("war_23")
        self.listview_war_2 = QtWidgets.QListWidget(self.widget_3)
        self.listview_war_2.setGeometry(QtCore.QRect(390, 45, 121, 211))
        font = QtGui.QFont()
        font.setPointSize(9)
        self.listview_war_2.setFont(font)
        self.listview_war_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.listview_war_2.setFrameShape(QtWidgets.QFrame.Panel)
        self.listview_war_2.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.listview_war_2.setLineWidth(2)
        self.listview_war_2.setAutoScroll(True)
        self.listview_war_2.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        self.listview_war_2.setDefaultDropAction(QtCore.Qt.MoveAction)
        self.listview_war_2.setObjectName("listview_war_2")
        self.war_lockadd = QtWidgets.QPushButton(self.widget_3)
        self.war_lockadd.setGeometry(QtCore.QRect(390, 258, 121, 25))
        font = QtGui.QFont()
        font.setBold(False)
        font.setWeight(50)
        self.war_lockadd.setFont(font)
        self.war_lockadd.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_lockadd.setObjectName("war_lockadd")
        self.war_locksub = QtWidgets.QPushButton(self.widget_3)
        self.war_locksub.setGeometry(QtCore.QRect(390, 288, 121, 25))
        font = QtGui.QFont()
        font.setBold(False)
        font.setWeight(50)
        self.war_locksub.setFont(font)
        self.war_locksub.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_locksub.setObjectName("war_locksub")
        self.war_life = QtWidgets.QPushButton(self.widget_3)
        self.war_life.setEnabled(True)
        self.war_life.setGeometry(QtCore.QRect(550, 300, 121, 35))
        self.war_life.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0)")
        self.war_life.setObjectName("war_life")
        self.war_kill = QtWidgets.QPushButton(self.widget_3)
        self.war_kill.setGeometry(QtCore.QRect(590, 10, 71, 25))
        self.war_kill.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_kill.setObjectName("war_kill")
        self.war_input_24 = myTextEdit(self.widget_3)
        self.war_input_24.setGeometry(QtCore.QRect(590, 43, 71, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_24.setFont(font)
        self.war_input_24.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_24.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_24.setLineWidth(2)
        self.war_input_24.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_24.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_24.setObjectName("war_input_24")
        self.war_24 = QtWidgets.QLabel(self.widget_3)
        self.war_24.setGeometry(QtCore.QRect(520, 45, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_24.setFont(font)
        self.war_24.setStyleSheet("color:rgb(0,0,0);")
        self.war_24.setObjectName("war_24")
        self.war_25 = QtWidgets.QLabel(self.widget_3)
        self.war_25.setGeometry(QtCore.QRect(520, 75, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_25.setFont(font)
        self.war_25.setStyleSheet("color:rgb(0,0,0);")
        self.war_25.setObjectName("war_25")
        self.war_input_25 = myTextEdit(self.widget_3)
        self.war_input_25.setGeometry(QtCore.QRect(590, 73, 71, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_25.setFont(font)
        self.war_input_25.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_25.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_25.setLineWidth(2)
        self.war_input_25.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_25.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_25.setObjectName("war_input_25")
        self.war_input_26 = myTextEdit(self.widget_3)
        self.war_input_26.setGeometry(QtCore.QRect(590, 103, 71, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_26.setFont(font)
        self.war_input_26.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_26.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_26.setLineWidth(2)
        self.war_input_26.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_26.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_26.setObjectName("war_input_26")
        self.war_26 = QtWidgets.QLabel(self.widget_3)
        self.war_26.setGeometry(QtCore.QRect(520, 105, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_26.setFont(font)
        self.war_26.setStyleSheet("color:rgb(0,0,0);")
        self.war_26.setObjectName("war_26")
        self.war_input_27 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_27.setGeometry(QtCore.QRect(590, 133, 71, 25))
        self.war_input_27.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_27.setEditable(False)
        self.war_input_27.setObjectName("war_input_27")
        self.war_input_27.addItem("")
        self.war_input_27.addItem("")
        self.war_input_27.addItem("")
        self.war_input_27.addItem("")
        self.war_input_27.addItem("")
        self.war_27 = QtWidgets.QLabel(self.widget_3)
        self.war_27.setGeometry(QtCore.QRect(520, 135, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_27.setFont(font)
        self.war_27.setStyleSheet("color:rgb(0,0,0);")
        self.war_27.setObjectName("war_27")
        self.war_input_28 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_28.setGeometry(QtCore.QRect(535, 168, 61, 25))
        self.war_input_28.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_28.setEditable(False)
        self.war_input_28.setObjectName("war_input_28")
        self.war_input_28.addItem("")
        self.war_input_28.addItem("")
        self.war_input_28.addItem("")
        self.war_28 = QtWidgets.QLabel(self.widget_3)
        self.war_28.setGeometry(QtCore.QRect(511, 170, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_28.setFont(font)
        self.war_28.setStyleSheet("color:rgb(0,0,0);")
        self.war_28.setObjectName("war_28")
        self.war_29 = QtWidgets.QLabel(self.widget_3)
        self.war_29.setGeometry(QtCore.QRect(511, 200, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_29.setFont(font)
        self.war_29.setStyleSheet("color:rgb(0,0,0);")
        self.war_29.setObjectName("war_29")
        self.war_input_29 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_29.setGeometry(QtCore.QRect(535, 198, 61, 25))
        self.war_input_29.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_29.setEditable(False)
        self.war_input_29.setObjectName("war_input_29")
        self.war_input_29.addItem("")
        self.war_input_29.addItem("")
        self.war_input_29.addItem("")
        self.war_input_29.addItem("")
        self.war_input_30 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_30.setGeometry(QtCore.QRect(615, 198, 61, 25))
        self.war_input_30.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_30.setEditable(False)
        self.war_input_30.setObjectName("war_input_30")
        self.war_input_30.addItem("")
        self.war_input_30.addItem("")
        self.war_input_30.addItem("")
        self.war_input_30.addItem("")
        self.war_30 = QtWidgets.QLabel(self.widget_3)
        self.war_30.setGeometry(QtCore.QRect(591, 200, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_30.setFont(font)
        self.war_30.setStyleSheet("color:rgb(0,0,0);")
        self.war_30.setObjectName("war_30")
        self.war_31 = QtWidgets.QLabel(self.widget_3)
        self.war_31.setGeometry(QtCore.QRect(511, 230, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_31.setFont(font)
        self.war_31.setStyleSheet("color:rgb(0,0,0);")
        self.war_31.setObjectName("war_31")
        self.war_input_31 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_31.setGeometry(QtCore.QRect(535, 228, 61, 25))
        self.war_input_31.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_31.setEditable(False)
        self.war_input_31.setObjectName("war_input_31")
        self.war_input_31.addItem("")
        self.war_input_31.addItem("")
        self.war_input_31.addItem("")
        self.war_input_31.addItem("")
        self.war_input_32 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_32.setGeometry(QtCore.QRect(615, 228, 61, 25))
        self.war_input_32.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_32.setEditable(False)
        self.war_input_32.setObjectName("war_input_32")
        self.war_input_32.addItem("")
        self.war_input_32.addItem("")
        self.war_input_32.addItem("")
        self.war_input_32.addItem("")
        self.war_32 = QtWidgets.QLabel(self.widget_3)
        self.war_32.setGeometry(QtCore.QRect(591, 230, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_32.setFont(font)
        self.war_32.setStyleSheet("color:rgb(0,0,0);")
        self.war_32.setObjectName("war_32")
        self.war_input_33 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_33.setGeometry(QtCore.QRect(535, 258, 61, 25))
        self.war_input_33.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_33.setEditable(False)
        self.war_input_33.setObjectName("war_input_33")
        self.war_input_33.addItem("")
        self.war_input_33.addItem("")
        self.war_input_33.addItem("")
        self.war_input_33.addItem("")
        self.war_33 = QtWidgets.QLabel(self.widget_3)
        self.war_33.setGeometry(QtCore.QRect(511, 260, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_33.setFont(font)
        self.war_33.setStyleSheet("color:rgb(0,0,0);")
        self.war_33.setObjectName("war_33")
        self.war_input_34 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_34.setGeometry(QtCore.QRect(615, 258, 61, 25))
        self.war_input_34.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_34.setEditable(False)
        self.war_input_34.setObjectName("war_input_34")
        self.war_input_34.addItem("")
        self.war_input_34.addItem("")
        self.war_input_34.addItem("")
        self.war_input_34.addItem("")
        self.war_34 = QtWidgets.QLabel(self.widget_3)
        self.war_34.setGeometry(QtCore.QRect(591, 260, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_34.setFont(font)
        self.war_34.setStyleSheet("color:rgb(0,0,0);")
        self.war_34.setObjectName("war_34")
        self.war_input_35 = QtWidgets.QComboBox(self.widget_3)
        self.war_input_35.setGeometry(QtCore.QRect(615, 168, 61, 25))
        self.war_input_35.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_35.setEditable(False)
        self.war_input_35.setObjectName("war_input_35")
        self.war_input_35.addItem("")
        self.war_input_35.addItem("")
        self.war_input_35.addItem("")
        self.war_input_35.addItem("")
        self.war_35 = QtWidgets.QLabel(self.widget_3)
        self.war_35.setGeometry(QtCore.QRect(591, 170, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_35.setFont(font)
        self.war_35.setStyleSheet("color:rgb(0,0,0);")
        self.war_35.setObjectName("war_35")
        self.war_input_36 = myTextEdit(self.widget_3)
        self.war_input_36.setGeometry(QtCore.QRect(305, 323, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_36.setFont(font)
        self.war_input_36.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_36.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_36.setLineWidth(2)
        self.war_input_36.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_36.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_36.setObjectName("war_input_36")
        self.war_input_37 = myTextEdit(self.widget_3)
        self.war_input_37.setGeometry(QtCore.QRect(370, 323, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_37.setFont(font)
        self.war_input_37.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_37.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_37.setLineWidth(2)
        self.war_input_37.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_37.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_37.setObjectName("war_input_37")
        self.war_36 = QtWidgets.QLabel(self.widget_3)
        self.war_36.setGeometry(QtCore.QRect(280, 326, 21, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_36.setFont(font)
        self.war_36.setStyleSheet("color:rgb(0,0,0);")
        self.war_36.setObjectName("war_36")
        self.war_37 = QtWidgets.QLabel(self.widget_3)
        self.war_37.setGeometry(QtCore.QRect(345, 326, 21, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setBold(True)
        font.setWeight(75)
        self.war_37.setFont(font)
        self.war_37.setStyleSheet("color:rgb(0,0,0);")
        self.war_37.setObjectName("war_37")
        self.war_39 = QtWidgets.QLabel(self.widget_3)
        self.war_39.setGeometry(QtCore.QRect(476, 326, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_39.setFont(font)
        self.war_39.setStyleSheet("color:rgb(0,0,0);")
        self.war_39.setObjectName("war_39")
        self.war_input_38 = myTextEdit(self.widget_3)
        self.war_input_38.setGeometry(QtCore.QRect(435, 323, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_38.setFont(font)
        self.war_input_38.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_38.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_38.setLineWidth(2)
        self.war_input_38.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_38.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_38.setObjectName("war_input_38")
        self.war_input_39 = myTextEdit(self.widget_3)
        self.war_input_39.setGeometry(QtCore.QRect(500, 323, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_39.setFont(font)
        self.war_input_39.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_39.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_39.setLineWidth(2)
        self.war_input_39.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_39.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_39.setObjectName("war_input_39")
        self.war_38 = QtWidgets.QLabel(self.widget_3)
        self.war_38.setGeometry(QtCore.QRect(411, 326, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_38.setFont(font)
        self.war_38.setStyleSheet("color:rgb(0,0,0);")
        self.war_38.setObjectName("war_38")
        self.war_input_40 = myTextEdit(self.widget_3)
        self.war_input_40.setGeometry(QtCore.QRect(170, 350, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_40.setFont(font)
        self.war_input_40.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_40.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_40.setLineWidth(2)
        self.war_input_40.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_40.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_40.setObjectName("war_input_40")
        self.war_input_41 = myTextEdit(self.widget_3)
        self.war_input_41.setGeometry(QtCore.QRect(305, 350, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_41.setFont(font)
        self.war_input_41.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_41.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_41.setLineWidth(2)
        self.war_input_41.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_41.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_41.setObjectName("war_input_41")
        self.war_input_42 = myTextEdit(self.widget_3)
        self.war_input_42.setGeometry(QtCore.QRect(435, 350, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_42.setFont(font)
        self.war_input_42.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_42.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_42.setLineWidth(2)
        self.war_input_42.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_42.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_42.setObjectName("war_input_42")
        self.war_input_43 = myTextEdit(self.widget_3)
        self.war_input_43.setGeometry(QtCore.QRect(500, 350, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_43.setFont(font)
        self.war_input_43.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_43.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_43.setLineWidth(2)
        self.war_input_43.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_43.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_43.setObjectName("war_input_43")
        self.war_input_44 = myTextEdit(self.widget_3)
        self.war_input_44.setGeometry(QtCore.QRect(235, 350, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_44.setFont(font)
        self.war_input_44.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_44.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_44.setLineWidth(2)
        self.war_input_44.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_44.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_44.setObjectName("war_input_44")
        self.war_40 = QtWidgets.QLabel(self.widget_3)
        self.war_40.setGeometry(QtCore.QRect(410, 353, 21, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_40.setFont(font)
        self.war_40.setStyleSheet("color:rgb(0,0,0);")
        self.war_40.setObjectName("war_40")
        self.war_41 = QtWidgets.QLabel(self.widget_3)
        self.war_41.setGeometry(QtCore.QRect(145, 353, 21, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_41.setFont(font)
        self.war_41.setStyleSheet("color:rgb(0,0,0);")
        self.war_41.setObjectName("war_41")
        self.war_42 = QtWidgets.QLabel(self.widget_3)
        self.war_42.setGeometry(QtCore.QRect(345, 353, 21, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_42.setFont(font)
        self.war_42.setStyleSheet("color:rgb(0,0,0);")
        self.war_42.setObjectName("war_42")
        self.war_input_45 = myTextEdit(self.widget_3)
        self.war_input_45.setGeometry(QtCore.QRect(370, 350, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_45.setFont(font)
        self.war_input_45.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_45.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_45.setLineWidth(2)
        self.war_input_45.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_45.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_45.setObjectName("war_input_45")
        self.war_43 = QtWidgets.QLabel(self.widget_3)
        self.war_43.setGeometry(QtCore.QRect(211, 353, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_43.setFont(font)
        self.war_43.setStyleSheet("color:rgb(0,0,0);")
        self.war_43.setObjectName("war_43")
        self.war_44 = QtWidgets.QLabel(self.widget_3)
        self.war_44.setGeometry(QtCore.QRect(476, 353, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_44.setFont(font)
        self.war_44.setStyleSheet("color:rgb(0,0,0);")
        self.war_44.setObjectName("war_44")
        self.war_45 = QtWidgets.QLabel(self.widget_3)
        self.war_45.setGeometry(QtCore.QRect(281, 353, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_45.setFont(font)
        self.war_45.setStyleSheet("color:rgb(0,0,0);")
        self.war_45.setObjectName("war_45")
        self.war_input_46 = QtWidgets.QCheckBox(self.widget_3)
        self.war_input_46.setGeometry(QtCore.QRect(365, 46, 21, 19))
        self.war_input_46.setText("")
        self.war_input_46.setChecked(False)
        self.war_input_46.setObjectName("war_input_46")
        self.war_46 = QtWidgets.QLabel(self.widget_3)
        self.war_46.setGeometry(QtCore.QRect(340, 45, 20, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.war_46.setFont(font)
        self.war_46.setStyleSheet("color:rgb(0,0,0);")
        self.war_46.setObjectName("war_46")
        self.war_input_50 = QtWidgets.QTextEdit(self.widget_3)
        self.war_input_50.setGeometry(QtCore.QRect(540, 10, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_50.setFont(font)
        self.war_input_50.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_50.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_50.setLineWidth(2)
        self.war_input_50.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_50.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_50.setObjectName("war_input_50")
        self.war_input_48 = QtWidgets.QTextEdit(self.widget_3)
        self.war_input_48.setGeometry(QtCore.QRect(440, 10, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_48.setFont(font)
        self.war_input_48.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_48.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_48.setLineWidth(2)
        self.war_input_48.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_48.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_48.setObjectName("war_input_48")
        self.war_input_49 = QtWidgets.QTextEdit(self.widget_3)
        self.war_input_49.setGeometry(QtCore.QRect(490, 10, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_49.setFont(font)
        self.war_input_49.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_49.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_49.setLineWidth(2)
        self.war_input_49.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_49.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_49.setObjectName("war_input_49")
        self.war_input_47 = QtWidgets.QTextEdit(self.widget_3)
        self.war_input_47.setGeometry(QtCore.QRect(390, 10, 41, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_47.setFont(font)
        self.war_input_47.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.war_input_47.setFrameShape(QtWidgets.QFrame.Panel)
        self.war_input_47.setLineWidth(2)
        self.war_input_47.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_47.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_47.setObjectName("war_input_47")
        self.war_input_51 = QtWidgets.QLineEdit(self.widget_3)
        self.war_input_51.setGeometry(QtCore.QRect(185, 170, 25, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_51.setFont(font)
        self.war_input_51.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        #self.war_input_51.setFrameShape(QtWidgets.QFrame.Panel)
        #self.war_input_51.setLineWidth(2)
        #self.war_input_51.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        #self.war_input_51.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_51.setObjectName("war_input_51")
        self.war_input_52 = QtWidgets.QLineEdit(self.widget_3)
        self.war_input_52.setGeometry(QtCore.QRect(255, 170, 25, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_52.setFont(font)
        self.war_input_52.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        #self.war_input_52.setFrameShape(QtWidgets.QFrame.Panel)
        #self.war_input_52.setLineWidth(2)
        #self.war_input_52.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        #self.war_input_52.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_52.setObjectName("war_input_52")
        self.war_input_53 = QtWidgets.QLineEdit(self.widget_3)
        self.war_input_53.setGeometry(QtCore.QRect(185, 200, 25, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_53.setFont(font)
        self.war_input_53.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        #self.war_input_53.setFrameShape(QtWidgets.QFrame.Panel)
        #self.war_input_53.setLineWidth(2)
        #self.war_input_53.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        #self.war_input_53.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_53.setObjectName("war_input_53")
        self.war_input_54 = QtWidgets.QLineEdit(self.widget_3)
        self.war_input_54.setGeometry(QtCore.QRect(255, 200, 25, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.war_input_54.setFont(font)
        self.war_input_54.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        #self.war_input_54.setFrameShape(QtWidgets.QFrame.Panel)
        #self.war_input_54.setLineWidth(2)
        #self.war_input_54.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        #self.war_input_54.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.war_input_54.setObjectName("war_input_54")
        self.widget_0 = QtWidgets.QWidget(self.frame)
        self.widget_0.setEnabled(True)
        self.widget_0.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_0.setObjectName("widget_0")
        self.label = QtWidgets.QLabel(self.widget_0)
        self.label.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.label.setStyleSheet("background-color:rgb(253, 255, 244)")
        self.label.setText("")
        self.label.setObjectName("label")
        self.widget_4 = QtWidgets.QWidget(self.frame)
        self.widget_4.setEnabled(True)
        self.widget_4.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_4.setObjectName("widget_4")
        self.listview_item = QtWidgets.QListWidget(self.widget_4)
        self.listview_item.setGeometry(QtCore.QRect(10, 13, 145, 365))
        font = QtGui.QFont()
        font.setPointSize(9)
        self.listview_item.setFont(font)
        self.listview_item.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.listview_item.setFrameShape(QtWidgets.QFrame.Panel)
        self.listview_item.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.listview_item.setLineWidth(2)
        self.listview_item.setAutoScroll(True)
        self.listview_item.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        self.listview_item.setDefaultDropAction(QtCore.Qt.MoveAction)
        self.listview_item.setObjectName("listview_item")
        self.item_save = QtWidgets.QPushButton(self.widget_4)
        self.item_save.setGeometry(QtCore.QRect(520, 320, 121, 51))
        self.item_save.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_save.setObjectName("item_save")
        self.item_1 = QtWidgets.QLabel(self.widget_4)
        self.item_1.setGeometry(QtCore.QRect(175, 25, 41, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.item_1.setFont(font)
        self.item_1.setStyleSheet("color:rgb(0,0,0);")
        self.item_1.setObjectName("item_1")
        self.item_input_1 = QtWidgets.QComboBox(self.widget_4)
        self.item_input_1.setGeometry(QtCore.QRect(185, 50, 136, 25))
        self.item_input_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_input_1.setObjectName("item_input_1")
        self.item_2 = QtWidgets.QLabel(self.widget_4)
        self.item_2.setGeometry(QtCore.QRect(185, 100, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.item_2.setFont(font)
        self.item_2.setStyleSheet("color:rgb(0,0,0);")
        self.item_2.setObjectName("item_2")
        self.item_input_2 = myTextEdit(self.widget_4)
        self.item_input_2.setGeometry(QtCore.QRect(185, 125, 131, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.item_input_2.setFont(font)
        self.item_input_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_input_2.setFrameShape(QtWidgets.QFrame.Panel)
        self.item_input_2.setLineWidth(2)
        self.item_input_2.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_2.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_2.setObjectName("item_input_2")
        self.item_3 = QtWidgets.QLabel(self.widget_4)
        self.item_3.setGeometry(QtCore.QRect(185, 175, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.item_3.setFont(font)
        self.item_3.setStyleSheet("color:rgb(0,0,0);")
        self.item_3.setObjectName("item_3")
        self.item_input_3 = myTextEdit(self.widget_4)
        self.item_input_3.setGeometry(QtCore.QRect(185, 200, 131, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.item_input_3.setFont(font)
        self.item_input_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_input_3.setFrameShape(QtWidgets.QFrame.Panel)
        self.item_input_3.setLineWidth(2)
        self.item_input_3.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_3.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_3.setObjectName("item_input_3")
        self.item_all_1 = QtWidgets.QPushButton(self.widget_4)
        self.item_all_1.setGeometry(QtCore.QRect(185, 250, 131, 25))
        self.item_all_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_all_1.setObjectName("item_all_1")
        self.item_all_2 = QtWidgets.QPushButton(self.widget_4)
        self.item_all_2.setGeometry(QtCore.QRect(185, 290, 131, 25))
        self.item_all_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_all_2.setObjectName("item_all_2")
        self.item_all_3 = QtWidgets.QPushButton(self.widget_4)
        self.item_all_3.setGeometry(QtCore.QRect(185, 330, 131, 25))
        self.item_all_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_all_3.setObjectName("item_all_3")
        self.listview_item_2 = QtWidgets.QListWidget(self.widget_4)
        self.listview_item_2.setGeometry(QtCore.QRect(350, 13, 145, 365))
        font = QtGui.QFont()
        font.setPointSize(9)
        self.listview_item_2.setFont(font)
        self.listview_item_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.listview_item_2.setFrameShape(QtWidgets.QFrame.Panel)
        self.listview_item_2.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.listview_item_2.setLineWidth(2)
        self.listview_item_2.setAutoScroll(True)
        self.listview_item_2.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        self.listview_item_2.setDefaultDropAction(QtCore.Qt.MoveAction)
        self.listview_item_2.setObjectName("listview_item_2")
        self.item_4 = QtWidgets.QLabel(self.widget_4)
        self.item_4.setGeometry(QtCore.QRect(520, 25, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.item_4.setFont(font)
        self.item_4.setStyleSheet("color:rgb(0,0,0);")
        self.item_4.setObjectName("item_4")
        self.item_input_4 = myTextEdit(self.widget_4)
        self.item_input_4.setGeometry(QtCore.QRect(520, 50, 131, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.item_input_4.setFont(font)
        self.item_input_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_input_4.setFrameShape(QtWidgets.QFrame.Panel)
        self.item_input_4.setLineWidth(2)
        self.item_input_4.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_4.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_4.setObjectName("item_input_4")
        self.item_5 = QtWidgets.QLabel(self.widget_4)
        self.item_5.setGeometry(QtCore.QRect(520, 100, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.item_5.setFont(font)
        self.item_5.setStyleSheet("color:rgb(0,0,0);")
        self.item_5.setObjectName("item_5")
        self.item_input_5 = myTextEdit(self.widget_4)
        self.item_input_5.setGeometry(QtCore.QRect(520, 125, 131, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.item_input_5.setFont(font)
        self.item_input_5.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_input_5.setFrameShape(QtWidgets.QFrame.Panel)
        self.item_input_5.setLineWidth(2)
        self.item_input_5.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_5.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_5.setObjectName("item_input_5")
        self.item_input_6 = myTextEdit(self.widget_4)
        self.item_input_6.setGeometry(QtCore.QRect(520, 200, 131, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.item_input_6.setFont(font)
        self.item_input_6.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_input_6.setFrameShape(QtWidgets.QFrame.Panel)
        self.item_input_6.setLineWidth(2)
        self.item_input_6.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_6.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_6.setObjectName("item_input_6")
        self.item_6 = QtWidgets.QLabel(self.widget_4)
        self.item_6.setGeometry(QtCore.QRect(505, 175, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.item_6.setFont(font)
        self.item_6.setStyleSheet("color:rgb(0,0,0);")
        self.item_6.setObjectName("item_6")
        ''''''
        self.item_input_7 = myTextEdit(self.widget_4)
        self.item_input_7.setGeometry(QtCore.QRect(520, 275, 131, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.item_input_7.setFont(font)
        self.item_input_7.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_input_7.setFrameShape(QtWidgets.QFrame.Panel)
        self.item_input_7.setLineWidth(2)
        self.item_input_7.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_7.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.item_input_7.setObjectName("item_input_7")
        self.item_7 = QtWidgets.QLabel(self.widget_4)
        self.item_7.setGeometry(QtCore.QRect(505, 250, 61, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.item_7.setFont(font)
        self.item_7.setStyleSheet("color:rgb(0,0,0);")
        self.item_7.setObjectName("item_7")
        ''''''
        self.widget_5 = QtWidgets.QWidget(self.frame)
        self.widget_5.setEnabled(True)
        self.widget_5.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_5.setObjectName("widget_5")
        self.listview_item_3 = QtWidgets.QListWidget(self.widget_5)
        self.listview_item_3.setGeometry(QtCore.QRect(10, 13, 165, 365))
        font = QtGui.QFont()
        font.setPointSize(9)
        self.listview_item_3.setFont(font)
        self.listview_item_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.listview_item_3.setFrameShape(QtWidgets.QFrame.Panel)
        self.listview_item_3.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.listview_item_3.setLineWidth(2)
        self.listview_item_3.setAutoScroll(True)
        self.listview_item_3.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        self.listview_item_3.setDefaultDropAction(QtCore.Qt.MoveAction)
        self.listview_item_3.setObjectName("listview_item_3")
        self.item_save_2 = QtWidgets.QPushButton(self.widget_5)
        self.item_save_2.setGeometry(QtCore.QRect(550, 320, 121, 51))
        self.item_save_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_save_2.setObjectName("item_save_2")
        self.power_1 = QtWidgets.QLabel(self.widget_5)
        self.power_1.setGeometry(QtCore.QRect(185, 15, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.power_1.setFont(font)
        self.power_1.setStyleSheet("color:rgb(0,0,0);")
        self.power_1.setObjectName("power_1")
        self.power_input_1_1 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_1_1.setGeometry(QtCore.QRect(185, 40, 136, 25))
        self.power_input_1_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_1.setObjectName("power_input_1_1")
        self.power_2 = QtWidgets.QLabel(self.widget_5)
        self.power_2.setGeometry(QtCore.QRect(185, 115, 41, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.power_2.setFont(font)
        self.power_2.setStyleSheet("color:rgb(0,0,0);")
        self.power_2.setObjectName("power_2")
        self.power_3 = QtWidgets.QLabel(self.widget_5)
        self.power_3.setGeometry(QtCore.QRect(185, 215, 31, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.power_3.setFont(font)
        self.power_3.setStyleSheet("color:rgb(0,0,0);")
        self.power_3.setObjectName("power_3")
        self.power_input_1_2 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_1_2.setGeometry(QtCore.QRect(355, 40, 136, 25))
        self.power_input_1_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_2.setObjectName("power_input_1_2")
        self.power_input_1_3 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_1_3.setGeometry(QtCore.QRect(525, 40, 136, 25))
        self.power_input_1_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_3.setObjectName("power_input_1_3")
        self.power_input_1_4 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_1_4.setGeometry(QtCore.QRect(185, 80, 136, 25))
        self.power_input_1_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_4.setObjectName("power_input_1_4")
        self.power_input_1_5 = myTextEdit(self.widget_5)
        self.power_input_1_5.setGeometry(QtCore.QRect(355, 80, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.power_input_1_5.setFont(font)
        self.power_input_1_5.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_5.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_1_5.setLineWidth(2)
        self.power_input_1_5.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_5.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_5.setObjectName("power_input_1_5")
        self.power_label_1_2 = QtWidgets.QLabel(self.widget_5)
        self.power_label_1_2.setGeometry(QtCore.QRect(355, 15, 136, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.power_label_1_2.setFont(font)
        self.power_label_1_2.setStyleSheet("color:rgb(0,0,0);")
        self.power_label_1_2.setObjectName("power_label_1_2")
        self.power_label_1_3 = QtWidgets.QLabel(self.widget_5)
        self.power_label_1_3.setGeometry(QtCore.QRect(525, 15, 136, 20))
        self.power_label_1_3.setFont(font)
        self.power_label_1_3.setStyleSheet("color:rgb(0,0,0);")
        self.power_label_1_3.setObjectName("power_label_1_3")
        self.power_label_1_4 = QtWidgets.QLabel(self.widget_5)
        self.power_label_1_4.setGeometry(QtCore.QRect(185, 65, 136, 15))
        self.power_label_1_4.setFont(font)
        self.power_label_1_4.setStyleSheet("color:rgb(0,0,0);")
        self.power_label_1_4.setObjectName("power_label_1_4")
        self.power_label_1_5 = QtWidgets.QLabel(self.widget_5)
        self.power_label_1_5.setGeometry(QtCore.QRect(355, 65, 136, 15))
        self.power_label_1_5.setFont(font)
        self.power_label_1_5.setStyleSheet("color:rgb(0,0,0);")
        self.power_label_1_5.setObjectName("power_label_1_5")
        self.power_label_1_6 = QtWidgets.QLabel(self.widget_5)
        self.power_label_1_6.setGeometry(QtCore.QRect(525, 65, 136, 15))
        self.power_label_1_6.setFont(font)
        self.power_label_1_6.setStyleSheet("color:rgb(0,0,0);")
        self.power_label_1_6.setObjectName("power_label_1_6")
        value_font = QtGui.QFont()
        value_font.setFamily("微软雅黑")
        value_font.setPointSize(9)
        self.power_input_1_6 = myTextEdit(self.widget_5)
        self.power_input_1_6.setGeometry(QtCore.QRect(455, 40, 36, 25))
        self.power_input_1_6.setFont(value_font)
        self.power_input_1_6.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_6.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_1_6.setLineWidth(2)
        self.power_input_1_6.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_6.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_6.setObjectName("power_input_1_6")
        self.power_input_1_7 = myTextEdit(self.widget_5)
        self.power_input_1_7.setGeometry(QtCore.QRect(625, 40, 36, 25))
        self.power_input_1_7.setFont(value_font)
        self.power_input_1_7.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_7.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_1_7.setLineWidth(2)
        self.power_input_1_7.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_7.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_7.setObjectName("power_input_1_7")
        self.power_input_1_8 = myTextEdit(self.widget_5)
        self.power_input_1_8.setGeometry(QtCore.QRect(285, 80, 36, 25))
        self.power_input_1_8.setFont(value_font)
        self.power_input_1_8.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_8.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_1_8.setLineWidth(2)
        self.power_input_1_8.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_8.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_8.setObjectName("power_input_1_8")
        self.power_input_1_9 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_1_9.setGeometry(QtCore.QRect(355, 80, 96, 25))
        self.power_input_1_9.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_9.setObjectName("power_input_1_9")
        self.power_input_1_10 = myTextEdit(self.widget_5)
        self.power_input_1_10.setGeometry(QtCore.QRect(455, 80, 36, 25))
        self.power_input_1_10.setFont(value_font)
        self.power_input_1_10.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_10.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_1_10.setLineWidth(2)
        self.power_input_1_10.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_10.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_10.setObjectName("power_input_1_10")
        self.power_input_1_11 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_1_11.setGeometry(QtCore.QRect(525, 80, 96, 25))
        self.power_input_1_11.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_11.setObjectName("power_input_1_11")
        self.power_input_1_12 = myTextEdit(self.widget_5)
        self.power_input_1_12.setGeometry(QtCore.QRect(625, 80, 36, 25))
        self.power_input_1_12.setFont(value_font)
        self.power_input_1_12.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_1_12.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_1_12.setLineWidth(2)
        self.power_input_1_12.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_12.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_1_12.setObjectName("power_input_1_12")
        self.power_input_2_2 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_2_2.setGeometry(QtCore.QRect(355, 140, 136, 25))
        self.power_input_2_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_2_2.setObjectName("power_input_2_2")
        self.power_input_2_1 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_2_1.setGeometry(QtCore.QRect(185, 140, 136, 25))
        self.power_input_2_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_2_1.setObjectName("power_input_2_1")
        self.power_input_2_3 = myTextEdit(self.widget_5)
        self.power_input_2_3.setGeometry(QtCore.QRect(525, 140, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.power_input_2_3.setFont(font)
        self.power_input_2_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_2_3.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_2_3.setLineWidth(2)
        self.power_input_2_3.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_2_3.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_2_3.setObjectName("power_input_2_3")
        self.power_input_2_4 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_2_4.setGeometry(QtCore.QRect(185, 180, 136, 25))
        self.power_input_2_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_2_4.setObjectName("power_input_2_4")
        self.power_input_2_6 = myTextEdit(self.widget_5)
        self.power_input_2_6.setGeometry(QtCore.QRect(525, 180, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.power_input_2_6.setFont(font)
        self.power_input_2_6.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_2_6.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_2_6.setLineWidth(2)
        self.power_input_2_6.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_2_6.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_2_6.setObjectName("power_input_2_6")
        self.power_input_2_5 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_2_5.setGeometry(QtCore.QRect(355, 180, 136, 25))
        self.power_input_2_5.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_2_5.setObjectName("power_input_2_5")
        self.power_input_3_2 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_3_2.setGeometry(QtCore.QRect(315, 240, 120, 25))
        self.power_input_3_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_2.setObjectName("power_input_3_2")
        self.power_input_3_3 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_3_3.setGeometry(QtCore.QRect(445, 240, 120, 25))
        self.power_input_3_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_3.setObjectName("power_input_3_3")
        self.power_input_3_1 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_3_1.setGeometry(QtCore.QRect(185, 240, 120, 25))
        self.power_input_3_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_1.setObjectName("power_input_3_1")
        self.power_input_3_4 = myTextEdit(self.widget_5)
        self.power_input_3_4.setGeometry(QtCore.QRect(575, 240, 86, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.power_input_3_4.setFont(font)
        self.power_input_3_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_4.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_3_4.setLineWidth(2)
        self.power_input_3_4.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_3_4.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_3_4.setObjectName("power_input_3_4")
        self.power_input_3_6 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_3_6.setGeometry(QtCore.QRect(315, 280, 120, 25))
        self.power_input_3_6.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_6.setObjectName("power_input_3_6")
        self.power_input_3_7 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_3_7.setGeometry(QtCore.QRect(445, 280, 120, 25))
        self.power_input_3_7.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_7.setObjectName("power_input_3_7")
        self.power_input_3_5 = QtWidgets.QComboBox(self.widget_5)
        self.power_input_3_5.setGeometry(QtCore.QRect(185, 280, 120, 25))
        self.power_input_3_5.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_5.setObjectName("power_input_3_5")
        self.power_input_3_8 = myTextEdit(self.widget_5)
        self.power_input_3_8.setGeometry(QtCore.QRect(575, 280, 86, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.power_input_3_8.setFont(font)
        self.power_input_3_8.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.power_input_3_8.setFrameShape(QtWidgets.QFrame.Panel)
        self.power_input_3_8.setLineWidth(2)
        self.power_input_3_8.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_3_8.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.power_input_3_8.setObjectName("power_input_3_8")
        self.widget_6 = QtWidgets.QWidget(self.frame)
        self.widget_6.setEnabled(True)
        self.widget_6.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_6.setObjectName("widget_6")
        self.listview_var_1 = QtWidgets.QTableWidget(self.widget_6)
        self.listview_var_1.setGeometry(QtCore.QRect(10, 50, 212, 331))
        self.listview_var_1.setObjectName("listview_var_1")
        self.listview_var_1.setColumnCount(3)
        self.listview_var_1.setRowCount(0)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_1.setHorizontalHeaderItem(0, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_1.setHorizontalHeaderItem(1, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_1.setHorizontalHeaderItem(2, item)
        self.listview_var_1.horizontalHeader().setDefaultSectionSize(70)
        self.listview_var_1.verticalHeader().setVisible(False)
        self.listview_var_1.verticalHeader().setDefaultSectionSize(20)
        self.listview_var_2 = QtWidgets.QTableWidget(self.widget_6)
        self.listview_var_2.setGeometry(QtCore.QRect(230, 50, 212, 331))
        self.listview_var_2.setObjectName("listview_var_2")
        self.listview_var_2.setColumnCount(3)
        self.listview_var_2.setRowCount(0)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_2.setHorizontalHeaderItem(0, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_2.setHorizontalHeaderItem(1, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_2.setHorizontalHeaderItem(2, item)
        self.listview_var_2.horizontalHeader().setDefaultSectionSize(70)
        self.listview_var_2.verticalHeader().setVisible(False)
        self.listview_var_2.verticalHeader().setDefaultSectionSize(20)
        self.listview_var_3 = QtWidgets.QTableWidget(self.widget_6)
        self.listview_var_3.setGeometry(QtCore.QRect(450, 50, 221, 331))
        self.listview_var_3.setObjectName("listview_var_3")
        self.listview_var_3.setColumnCount(3)
        self.listview_var_3.setRowCount(0)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_3.setHorizontalHeaderItem(0, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_3.setHorizontalHeaderItem(1, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_var_3.setHorizontalHeaderItem(2, item)
        self.listview_var_3.horizontalHeader().setDefaultSectionSize(70)
        self.listview_var_3.verticalHeader().setVisible(False)
        self.listview_var_3.verticalHeader().setDefaultSectionSize(20)
        self.var_input_1 = myTextEdit(self.widget_6)
        self.var_input_1.setGeometry(QtCore.QRect(10, 20, 102, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.var_input_1.setFont(font)
        self.var_input_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_input_1.setFrameShape(QtWidgets.QFrame.Panel)
        self.var_input_1.setLineWidth(2)
        self.var_input_1.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.var_input_1.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.var_input_1.setObjectName("var_input_1")
        self.var_input_2 = myTextEdit(self.widget_6)
        self.var_input_2.setGeometry(QtCore.QRect(230, 20, 102, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.var_input_2.setFont(font)
        self.var_input_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_input_2.setFrameShape(QtWidgets.QFrame.Panel)
        self.var_input_2.setLineWidth(2)
        self.var_input_2.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.var_input_2.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.var_input_2.setObjectName("var_input_2")
        self.var_input_3 = myTextEdit(self.widget_6)
        self.var_input_3.setGeometry(QtCore.QRect(450, 20, 102, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.var_input_3.setFont(font)
        self.var_input_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_input_3.setFrameShape(QtWidgets.QFrame.Panel)
        self.var_input_3.setLineWidth(2)
        self.var_input_3.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.var_input_3.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.var_input_3.setObjectName("var_input_3")
        self.var_input_4 = QtWidgets.QPushButton(self.widget_6)
        self.var_input_4.setGeometry(QtCore.QRect(120, 20, 50, 25))
        self.var_input_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_input_4.setObjectName("var_input_4")
        self.var_input_5 = QtWidgets.QPushButton(self.widget_6)
        self.var_input_5.setGeometry(QtCore.QRect(340, 20, 50, 25))
        self.var_input_5.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_input_5.setObjectName("var_input_5")
        self.var_input_6 = QtWidgets.QPushButton(self.widget_6)
        self.var_input_6.setGeometry(QtCore.QRect(560, 20, 50, 25))
        self.var_input_6.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_input_6.setObjectName("var_input_6")
        self.var_save_1 = QtWidgets.QPushButton(self.widget_6)
        self.var_save_1.setGeometry(QtCore.QRect(170, 20, 50, 25))
        self.var_save_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_save_2 = QtWidgets.QPushButton(self.widget_6)
        self.var_save_2.setGeometry(QtCore.QRect(390, 20, 50, 25))
        self.var_save_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.var_save_3 = QtWidgets.QPushButton(self.widget_6)
        self.var_save_3.setGeometry(QtCore.QRect(610, 20, 50, 25))
        self.var_save_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.widget_7 = QtWidgets.QWidget(self.frame)
        self.widget_7.setEnabled(True)
        self.widget_7.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_7.setObjectName("widget_7")
        self.listview_diy = QtWidgets.QTableWidget(self.widget_7)
        self.listview_diy.setGeometry(QtCore.QRect(10, 50, 661, 331))
        self.listview_diy.setObjectName("listview_diy")
        self.listview_diy.setColumnCount(6)
        self.listview_diy.setRowCount(0)
        item = QtWidgets.QTableWidgetItem()
        self.listview_diy.setHorizontalHeaderItem(0, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_diy.setHorizontalHeaderItem(1, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_diy.setHorizontalHeaderItem(2, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_diy.setHorizontalHeaderItem(3, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_diy.setHorizontalHeaderItem(4, item)
        item = QtWidgets.QTableWidgetItem()
        self.listview_diy.setHorizontalHeaderItem(5, item)
        self.listview_diy.horizontalHeader().setDefaultSectionSize(100)
        self.listview_diy.verticalHeader().setVisible(False)
        self.listview_diy.verticalHeader().setDefaultSectionSize(17)
        self.diy_input_1 = myTextEdit(self.widget_7)
        self.diy_input_1.setGeometry(QtCore.QRect(10, 20, 102, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.diy_input_2 = QtWidgets.QPushButton(self.widget_7)
        self.diy_input_2.setGeometry(QtCore.QRect(10, 20, 102, 25))
        self.diy_input_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.diy_input_2.setObjectName("diy_input_2")
        self.diy_input_4 = QtWidgets.QComboBox(self.widget_7)
        self.diy_input_4.setGeometry(QtCore.QRect(520, 20, 151, 25))
        self.diy_input_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.diy_input_4.setEditable(False)
        self.diy_input_4.setObjectName("diy_input_4")
        self.widget_8 = QtWidgets.QWidget(self.frame)
        self.widget_8.setEnabled(True)
        self.widget_8.setGeometry(QtCore.QRect(0, 0, 681, 391))
        self.widget_8.setObjectName("widget_8")
        self.listview_mk = QtWidgets.QListWidget(self.widget_8)
        self.listview_mk.setGeometry(QtCore.QRect(10, 13, 145, 365))
        font = QtGui.QFont()
        font.setPointSize(8)
        self.listview_mk.setFont(font)
        self.listview_mk.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.listview_mk.setFrameShape(QtWidgets.QFrame.Panel)
        self.listview_mk.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.listview_mk.setLineWidth(2)
        self.listview_mk.setAutoScroll(True)
        self.listview_mk.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        self.listview_mk.setDefaultDropAction(QtCore.Qt.MoveAction)
        self.listview_mk.setObjectName("listview_mk")
        self.item_save_4 = QtWidgets.QPushButton(self.widget_8)
        self.item_save_4.setGeometry(QtCore.QRect(550, 320, 121, 51))
        self.item_save_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.item_save_4.setObjectName("item_save_4")
        self.mk_1_1 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_1.setGeometry(QtCore.QRect(175, 40, 51, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_1.setFont(font)
        self.mk_1_1.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_1.setObjectName("mk_1_1")
        self.mk_input_1_1 = QtWidgets.QComboBox(self.widget_8)
        self.mk_input_1_1.setGeometry(QtCore.QRect(230, 40, 151, 25))
        self.mk_input_1_1.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_1.setObjectName("mk_input_1_1")
        self.mk_input_1_2 = myTextEdit(self.widget_8)
        self.mk_input_1_2.setGeometry(QtCore.QRect(480, 40, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.mk_input_1_2.setFont(font)
        self.mk_input_1_2.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_2.setFrameShape(QtWidgets.QFrame.Panel)
        self.mk_input_1_2.setLineWidth(2)
        self.mk_input_1_2.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_2.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_2.setObjectName("mk_input_1_2")
        self.mk_1_2 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_2.setGeometry(QtCore.QRect(405, 40, 71, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_2.setFont(font)
        self.mk_1_2.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_2.setObjectName("mk_1_2")
        self.mk_input_1_4 = myTextEdit(self.widget_8)
        self.mk_input_1_4.setGeometry(QtCore.QRect(480, 80, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.mk_input_1_4.setFont(font)
        self.mk_input_1_4.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_4.setFrameShape(QtWidgets.QFrame.Panel)
        self.mk_input_1_4.setLineWidth(2)
        self.mk_input_1_4.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_4.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_4.setObjectName("mk_input_1_4")
        self.mk_1_4 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_4.setGeometry(QtCore.QRect(405, 80, 71, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_4.setFont(font)
        self.mk_1_4.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_4.setObjectName("mk_1_4")
        self.mk_1_3 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_3.setGeometry(QtCore.QRect(175, 80, 51, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_3.setFont(font)
        self.mk_1_3.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_3.setObjectName("mk_1_3")
        self.mk_input_1_3 = QtWidgets.QComboBox(self.widget_8)
        self.mk_input_1_3.setGeometry(QtCore.QRect(230, 80, 151, 25))
        self.mk_input_1_3.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_3.setObjectName("mk_input_1_3")
        self.mk_input_1_6 = myTextEdit(self.widget_8)
        self.mk_input_1_6.setGeometry(QtCore.QRect(480, 120, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.mk_input_1_6.setFont(font)
        self.mk_input_1_6.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_6.setFrameShape(QtWidgets.QFrame.Panel)
        self.mk_input_1_6.setLineWidth(2)
        self.mk_input_1_6.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_6.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_6.setObjectName("mk_input_1_6")
        self.mk_1_5 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_5.setGeometry(QtCore.QRect(175, 120, 51, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_5.setFont(font)
        self.mk_1_5.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_5.setObjectName("mk_1_5")
        self.mk_input_1_5 = QtWidgets.QComboBox(self.widget_8)
        self.mk_input_1_5.setGeometry(QtCore.QRect(230, 120, 151, 25))
        self.mk_input_1_5.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_5.setObjectName("mk_input_1_5")
        self.mk_1_6 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_6.setGeometry(QtCore.QRect(405, 120, 71, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_6.setFont(font)
        self.mk_1_6.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_6.setObjectName("mk_1_6")
        self.mk_1_8 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_8.setGeometry(QtCore.QRect(405, 160, 71, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_8.setFont(font)
        self.mk_1_8.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_8.setObjectName("mk_1_8")
        self.mk_1_7 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_7.setGeometry(QtCore.QRect(175, 160, 51, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_7.setFont(font)
        self.mk_1_7.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_7.setObjectName("mk_1_7")
        self.mk_input_1_8 = myTextEdit(self.widget_8)
        self.mk_input_1_8.setGeometry(QtCore.QRect(480, 160, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.mk_input_1_8.setFont(font)
        self.mk_input_1_8.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_8.setFrameShape(QtWidgets.QFrame.Panel)
        self.mk_input_1_8.setLineWidth(2)
        self.mk_input_1_8.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_8.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_8.setObjectName("mk_input_1_8")
        self.mk_input_1_7 = QtWidgets.QComboBox(self.widget_8)
        self.mk_input_1_7.setGeometry(QtCore.QRect(230, 160, 151, 25))
        self.mk_input_1_7.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_7.setObjectName("mk_input_1_7")
        self.mk_1_10 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_10.setGeometry(QtCore.QRect(405, 200, 71, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_10.setFont(font)
        self.mk_1_10.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_10.setObjectName("mk_1_10")
        self.mk_1_9 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_9.setGeometry(QtCore.QRect(175, 200, 51, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_9.setFont(font)
        self.mk_1_9.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_9.setObjectName("mk_1_9")
        self.mk_input_1_9 = QtWidgets.QComboBox(self.widget_8)
        self.mk_input_1_9.setGeometry(QtCore.QRect(230, 200, 151, 25))
        self.mk_input_1_9.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_9.setObjectName("mk_input_1_9")
        self.mk_input_1_10 = myTextEdit(self.widget_8)
        self.mk_input_1_10.setGeometry(QtCore.QRect(480, 200, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.mk_input_1_10.setFont(font)
        self.mk_input_1_10.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_10.setFrameShape(QtWidgets.QFrame.Panel)
        self.mk_input_1_10.setLineWidth(2)
        self.mk_input_1_10.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_10.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_10.setObjectName("mk_input_1_10")
        self.mk_1_11 = QtWidgets.QLabel(self.widget_8)
        self.mk_1_11.setGeometry(QtCore.QRect(175, 250, 71, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.mk_1_11.setFont(font)
        self.mk_1_11.setStyleSheet("color:rgb(0,0,0);")
        self.mk_1_11.setObjectName("mk_1_11")
        self.mk_input_1_11 = QtWidgets.QTextEdit(self.widget_8)
        self.mk_input_1_11.setGeometry(QtCore.QRect(235, 250, 136, 25))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(9)
        self.mk_input_1_11.setFont(font)
        self.mk_input_1_11.setStyleSheet("background-color:rgb(255, 255, 255);\n"
"color:rgb(0,0,0);")
        self.mk_input_1_11.setFrameShape(QtWidgets.QFrame.Panel)
        self.mk_input_1_11.setLineWidth(2)
        self.mk_input_1_11.setVerticalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_11.setHorizontalScrollBarPolicy(QtCore.Qt.ScrollBarAlwaysOff)
        self.mk_input_1_11.setObjectName("mk_input_1_11")
        self.widget_1.raise_()
        self.widget_2.raise_()
        self.widget_3.raise_()
        self.widget_4.raise_()
        self.widget_5.raise_()
        self.widget_6.raise_()
        self.widget_0.raise_()
        self.widget_7.raise_()
        self.label_title = QtWidgets.QLabel(self.centralwidget)
        self.label_title.setGeometry(QtCore.QRect(18, 67, 36, 20))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        font.setPointSize(10)
        font.setBold(False)
        font.setWeight(50)
        self.label_title.setFont(font)
        self.label_title.setAutoFillBackground(True)
        self.label_title.setScaledContents(False)
        self.label_title.setAlignment(QtCore.Qt.AlignCenter)
        self.label_title.setObjectName("label_title")
        self.label_pivot = QtWidgets.QLabel(self.centralwidget)
        self.label_pivot.setGeometry(QtCore.QRect(10, 11, 52, 50))
        self.label_pivot.setAutoFillBackground(False)
        self.label_pivot.setStyleSheet("background-color:rgb(255, 255, 255)")
        self.label_pivot.setFrameShape(QtWidgets.QFrame.Panel)
        self.label_pivot.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.label_pivot.setLineWidth(2)
        self.label_pivot.setMidLineWidth(1)
        self.label_pivot.setText("")
        self.label_pivot.setObjectName("label_pivot")
        self.label_window1 = QtWidgets.QLabel(self.centralwidget)
        self.label_window1.setGeometry(QtCore.QRect(70, 11, 200, 24))
        self.label_window1.setAutoFillBackground(False)
        self.label_window1.setStyleSheet("background-color:rgb(5,5,5);\n"
"color:rgb(0,255,0);")
        self.label_window1.setFrameShape(QtWidgets.QFrame.Panel)
        self.label_window1.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.label_window1.setLineWidth(1)
        self.label_window1.setAlignment(QtCore.Qt.AlignLeading|QtCore.Qt.AlignLeft|QtCore.Qt.AlignVCenter)
        self.label_window1.setObjectName("label_window1")
        self.label_window2 = QtWidgets.QLabel(self.centralwidget)
        self.label_window2.setGeometry(QtCore.QRect(70, 36, 200, 24))
        self.label_window2.setAutoFillBackground(False)
        self.label_window2.setStyleSheet("background-color:rgb(5,5,5);\n"
"color:rgb(0,255,0);")
        self.label_window2.setFrameShape(QtWidgets.QFrame.Panel)
        self.label_window2.setFrameShadow(QtWidgets.QFrame.Sunken)
        self.label_window2.setLineWidth(1)
        self.label_window2.setObjectName("label_window2")
        self.label_help = QtWidgets.QLabel(self.centralwidget)
        self.label_help.setGeometry(QtCore.QRect(300, 30, 361, 16))
        font = QtGui.QFont()
        font.setFamily("黑体")
        font.setPointSize(9)
        font.setBold(False)
        font.setWeight(50)
        self.label_help.setFont(font)
        self.label_help.setStyleSheet("color:rgb(255, 0, 4);")
        self.label_help.setFrameShape(QtWidgets.QFrame.NoFrame)
        self.label_help.setFrameShadow(QtWidgets.QFrame.Plain)
        self.label_help.setTextFormat(QtCore.Qt.AutoText)
        self.label_help.setObjectName("label_help")
        self.version_0 = QtWidgets.QRadioButton(self.centralwidget)
        self.version_0.setGeometry(QtCore.QRect(210, 475, 50, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.version_0.setFont(font)
        self.version_0.setObjectName("version_0")
        self.version_1 = QtWidgets.QRadioButton(self.centralwidget)
        self.version_1.setGeometry(QtCore.QRect(20, 475, 60, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.version_1.setFont(font)
        self.version_1.setObjectName("version_1")
        self.version_5 = QtWidgets.QRadioButton(self.centralwidget)
        self.version_5.setGeometry(QtCore.QRect(90, 475, 50, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.version_5.setFont(font)
        self.version_5.setObjectName("version_5")
        self.version_6 = QtWidgets.QRadioButton(self.centralwidget)
        self.version_6.setGeometry(QtCore.QRect(150, 475, 50, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.version_6.setFont(font)
        self.version_6.setObjectName("version_6")
        self.version_2 = QtWidgets.QRadioButton(self.centralwidget)
        self.version_2.setGeometry(QtCore.QRect(270, 475, 50, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.version_2.setFont(font)
        self.version_2.setObjectName("version_2")
        self.version_3 = QtWidgets.QRadioButton(self.centralwidget)
        self.version_3.setGeometry(QtCore.QRect(330, 475, 50, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.version_3.setFont(font)
        self.version_3.setObjectName("version_3")
        self.version_4 = QtWidgets.QRadioButton(self.centralwidget)
        self.version_4.setGeometry(QtCore.QRect(390, 475, 50, 19))
        font = QtGui.QFont()
        font.setFamily("微软雅黑")
        self.version_4.setFont(font)
        self.version_4.setObjectName("version_4")
        self.buttonGroup=QtWidgets.QButtonGroup(self.centralwidget)
        self.buttonGroup.addButton(self.version_0)
        self.buttonGroup.addButton(self.version_1)
        self.buttonGroup.addButton(self.version_5)
        self.buttonGroup.addButton(self.version_6)
        self.buttonGroup.addButton(self.version_2)
        self.buttonGroup.addButton(self.version_3)
        self.buttonGroup.addButton(self.version_4)
        MainWindow.setCentralWidget(self.centralwidget)
        self.toolBar = QtWidgets.QToolBar(MainWindow)
        self.toolBar.setEnabled(False)
        self.toolBar.setMovable(False)
        self.toolBar.setObjectName("toolBar")
        MainWindow.addToolBar(QtCore.Qt.TopToolBarArea, self.toolBar)
        self.action_C = QtWidgets.QAction(MainWindow)
        self.action_C.setObjectName("action_C")
        self.action_M = QtWidgets.QAction(MainWindow)
        self.action_M.setObjectName("action_M")
        self.action_B = QtWidgets.QAction(MainWindow)
        self.action_B.setObjectName("action_B")
        self.action_W = QtWidgets.QAction(MainWindow)
        self.action_W.setObjectName("action_W")
        self.action_T = QtWidgets.QAction(MainWindow)
        self.action_T.setObjectName("action_T")
        self.action_V = QtWidgets.QAction(MainWindow)
        self.action_V.setObjectName("action_V")
        self.action_D = QtWidgets.QAction(MainWindow)
        self.action_D.setObjectName("action_D")
        self.action_K = QtWidgets.QAction(MainWindow)
        self.action_K.setObjectName("action_K")
        self.toolBar.addAction(self.action_C)
        self.toolBar.addAction(self.action_M)
        self.toolBar.addAction(self.action_B)
        self.toolBar.addAction(self.action_W)
        self.toolBar.addAction(self.action_T)
        self.toolBar.addAction(self.action_K)
        self.toolBar.addAction(self.action_V)
        self.toolBar.addAction(self.action_D)

        self.retranslateUi(MainWindow)
        self.otherSettings(MainWindow)
        QtCore.QMetaObject.connectSlotsByName(MainWindow)

    def retranslateUi(self, MainWindow):
        _translate = QtCore.QCoreApplication.translate
        MainWindow.setWindowTitle(_translate("MainWindow", "by Puluo"))
        self.checkBox_1.setText(_translate("MainWindow", "可控制敌军和友军"))
        self.checkBox_2.setText(_translate("MainWindow", "不分敌我"))
        self.checkBox_3.setText(_translate("MainWindow", "禁止AI行动"))
        self.checkBox_4.setText(_translate("MainWindow", "待命后可移动"))
        self.checkBox_5.setText(_translate("MainWindow", "移动力 = 255"))
        self.checkBox_6.setText(_translate("MainWindow", "穿越移动"))
        self.checkBox_7.setText(_translate("MainWindow", "自动回归"))
        self.checkBox_8.setText(_translate("MainWindow", "托管我军主动出击"))
        self.checkBox_9.setText(_translate("MainWindow", "我军自动复活"))
        self.data_1.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">R形象</p></body></html>"))
        self.data_2.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">S形象</p></body></html>"))
        self.data_3.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">头像</p></body></html>"))
        self.data_4.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">攻击力</p></body></html>"))
        self.data_5.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">防御力</p></body></html>"))
        self.data_6.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">精神力</p></body></html>"))
        self.data_7.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">爆发力</p></body></html>"))
        self.data_8.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">士气</p></body></html>"))
        self.data_9.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">HP上限</p></body></html>"))
        self.data_10.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">MP上限</p></body></html>"))
        self.data_11.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">我军</p></body></html>"))
        self.data_12.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">等级</p></body></html>"))
        self.data_13.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">经验</p></body></html>"))
        self.data_14.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">武力</p></body></html>"))
        self.data_15.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">统率</p></body></html>"))
        self.data_16.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">智力</p></body></html>"))
        self.data_17.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">敏捷</p></body></html>"))
        self.data_18.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">运气</p></body></html>"))
        self.data_19.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">出阵</p></body></html>"))
        self.data_20.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">撤退</p></body></html>"))
        self.data_21.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">武功勋</p></body></html>"))
        self.data_22.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">统功勋</p></body></html>"))
        self.data_23.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">智功勋</p></body></html>"))
        self.data_24.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">敏功勋</p></body></html>"))
        self.data_25.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">运功勋</p></body></html>"))
        self.data_26.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">幸存</p></body></html>"))
        self.data_27.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">6号(2</p></body></html>"))
        self.data_28.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">44号(2</p></body></html>"))
        self.data_29.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">46号(2</p></body></html>"))
        self.data_30.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">杀敌数</p></body></html>"))
        self.data_31.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">兵种</p></body></html>"))
        self.data_32.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">武器</p></body></html>"))
        self.data_33.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">等级</p></body></html>"))
        self.data_34.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">经验</p></body></html>"))
        self.data_35.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">防具</p></body></html>"))
        self.data_36.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">等级</p></body></html>"))
        self.data_37.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">经验</p></body></html>"))
        self.data_38.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">辅助</p></body></html>"))
        self.data_save.setText(_translate("MainWindow", "保存"))
        self.data_recal.setText(_translate("MainWindow", "能力重新计算"))
        self.war_2.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">HP</p></body></html>"))
        self.war_save.setText(_translate("MainWindow", "保存"))
        self.war_kind_1.setText(_translate("MainWindow", "我"))
        self.war_kind_2.setText(_translate("MainWindow", "友"))
        self.war_kind_3.setText(_translate("MainWindow", "敌"))
        self.war_connect.setText(_translate("MainWindow", "连接至人物"))
        self.war_3.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">MP</p></body></html>"))
        self.war_4.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">SP</p></body></html>"))
        self.war_5.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">麻痹</p></body></html>"))
        self.war_6.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">禁咒</p></body></html>"))
        self.war_7.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">混乱</p></body></html>"))
        self.war_8.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">中毒</p></body></html>"))
        self.war_9.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">X</p></body></html>"))
        self.war_10.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">Y</p></body></html>"))
        self.war_11.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">AI</p></body></html>"))
        self.war_input_11.setItemText(0, _translate("MainWindow", "被动出击"))
        self.war_input_11.setItemText(1, _translate("MainWindow", "主动出击"))
        self.war_input_11.setItemText(2, _translate("MainWindow", "坚守原地"))
        self.war_input_11.setItemText(3, _translate("MainWindow", "攻击武将"))
        self.war_input_11.setItemText(4, _translate("MainWindow", "到指定点"))
        self.war_input_11.setItemText(5, _translate("MainWindow", "跟随武将"))
        self.war_input_11.setItemText(6, _translate("MainWindow", "逃到指定点"))
        self.war_12.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">武将</p></body></html>"))
        self.war_13.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">X1</p></body></html>"))
        self.war_14.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">Y1</p></body></html>"))
        self.war_15.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">行动</p></body></html>"))
        self.war_16.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">方向</p></body></html>"))
        self.war_input_16.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_16.setItemText(1, _translate("MainWindow", "上"))
        self.war_input_16.setItemText(2, _translate("MainWindow", "右"))
        self.war_input_16.setItemText(3, _translate("MainWindow", "下"))
        self.war_input_16.setItemText(4, _translate("MainWindow", "左"))
        self.war_input_17.setItemText(0, _translate("MainWindow", "减少"))
        self.war_input_17.setItemText(1, _translate("MainWindow", "正常"))
        self.war_input_17.setItemText(2, _translate("MainWindow", "增加"))
        self.war_17.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">攻击</p></body></html>"))
        self.war_18.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">防御</p></body></html>"))
        self.war_input_18.setItemText(0, _translate("MainWindow", "减少"))
        self.war_input_18.setItemText(1, _translate("MainWindow", "正常"))
        self.war_input_18.setItemText(2, _translate("MainWindow", "增加"))
        self.war_input_19.setItemText(0, _translate("MainWindow", "减少"))
        self.war_input_19.setItemText(1, _translate("MainWindow", "正常"))
        self.war_input_19.setItemText(2, _translate("MainWindow", "增加"))
        self.war_19.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">精神</p></body></html>"))
        self.war_20.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">爆发</p></body></html>"))
        self.war_input_20.setItemText(0, _translate("MainWindow", "减少"))
        self.war_input_20.setItemText(1, _translate("MainWindow", "正常"))
        self.war_input_20.setItemText(2, _translate("MainWindow", "增加"))
        self.war_input_21.setItemText(0, _translate("MainWindow", "减少"))
        self.war_input_21.setItemText(1, _translate("MainWindow", "正常"))
        self.war_input_21.setItemText(2, _translate("MainWindow", "增加"))
        self.war_21.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">士气</p></body></html>"))
        self.war_input_22.setItemText(0, _translate("MainWindow", "减少"))
        self.war_input_22.setItemText(1, _translate("MainWindow", "正常"))
        self.war_input_22.setItemText(2, _translate("MainWindow", "增加"))
        self.war_22.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">移动</p></body></html>"))
        self.war_input_23.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_23.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_23.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_23.setItemText(3, _translate("MainWindow", "增加"))
        self.war_23.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">全部</p></body></html>"))
        self.war_lockadd.setText(_translate("MainWindow", "增加锁定"))
        self.war_locksub.setText(_translate("MainWindow", "移除锁定"))
        self.war_life.setText(_translate("MainWindow", "战场复活"))
        self.war_kill.setText(_translate("MainWindow", "区域灭敌"))
        self.war_24.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">回合上限</p></body></html>"))
        self.war_25.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">当前回合</p></body></html>"))
        self.war_26.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">下一关</p></body></html>"))
        self.war_input_27.setItemText(0, _translate("MainWindow", "晴"))
        self.war_input_27.setItemText(1, _translate("MainWindow", "阴"))
        self.war_input_27.setItemText(2, _translate("MainWindow", "小雨"))
        self.war_input_27.setItemText(3, _translate("MainWindow", "大雨"))
        self.war_input_27.setItemText(4, _translate("MainWindow", "雪"))
        self.war_27.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">天气</p></body></html>"))
        self.war_input_28.setItemText(0, _translate("MainWindow", "我"))
        self.war_input_28.setItemText(1, _translate("MainWindow", "友"))
        self.war_input_28.setItemText(2, _translate("MainWindow", "敌"))
        self.war_28.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">军</p></body></html>"))
        self.war_29.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">攻</p></body></html>"))
        self.war_input_29.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_29.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_29.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_29.setItemText(3, _translate("MainWindow", "增加"))
        self.war_input_30.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_30.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_30.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_30.setItemText(3, _translate("MainWindow", "增加"))
        self.war_30.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">防</p></body></html>"))
        self.war_31.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">精</p></body></html>"))
        self.war_input_31.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_31.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_31.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_31.setItemText(3, _translate("MainWindow", "增加"))
        self.war_input_32.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_32.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_32.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_32.setItemText(3, _translate("MainWindow", "增加"))
        self.war_32.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">爆</p></body></html>"))
        self.war_input_33.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_33.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_33.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_33.setItemText(3, _translate("MainWindow", "增加"))
        self.war_33.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">士</p></body></html>"))
        self.war_input_34.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_34.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_34.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_34.setItemText(3, _translate("MainWindow", "增加"))
        self.war_34.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">移</p></body></html>"))
        self.war_input_35.setItemText(0, _translate("MainWindow", "——"))
        self.war_input_35.setItemText(1, _translate("MainWindow", "减少"))
        self.war_input_35.setItemText(2, _translate("MainWindow", "正常"))
        self.war_input_35.setItemText(3, _translate("MainWindow", "增加"))
        self.war_35.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">全</p></body></html>"))
        self.war_36.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">X2</p></body></html>"))
        self.war_37.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">Y2</p></body></html>"))
        self.war_39.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">戮</p></body></html>"))
        self.war_38.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">22</p></body></html>"))
        self.war_40.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">2C</p></body></html>"))
        self.war_41.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">24</p></body></html>"))
        self.war_42.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">2A</p></body></html>"))
        self.war_43.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">26</p></body></html>"))
        self.war_44.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">2E</p></body></html>"))
        self.war_45.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">28</p></body></html>"))
        self.war_46.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">控</p></body></html>"))
        self.item_save.setText(_translate("MainWindow", "保存"))
        self.item_1.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\"><span style=\" font-size:9pt;\">装备</span></p></body></html>"))
        self.item_2.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">装备等级</p></body></html>"))
        self.item_3.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">装备经验</p></body></html>"))
        self.item_all_1.setText(_translate("MainWindow", "全宝物"))
        self.item_all_2.setText(_translate("MainWindow", "全道具"))
        self.item_all_3.setText(_translate("MainWindow", "清空仓库"))
        self.item_4.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">道具数量</p></body></html>"))
        self.item_5.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">金钱</p></body></html>"))
        self.item_6.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">忠奸度</p></body></html>"))
        self.item_7.setText(_translate("MainWindow", "<html><head/><body><p align=\"right\">功勋池</p></body></html>"))
        self.item_save_2.setText(_translate("MainWindow", "保存"))
        self.power_1.setText(_translate("MainWindow", "<html><head/><body><p>天赋</p></body></html>"))
        self.power_label_1_2.setText(_translate("MainWindow", "角色2 / 值"))
        self.power_label_1_3.setText(_translate("MainWindow", "角色3 / 值"))
        self.power_label_1_4.setText(_translate("MainWindow", "角色4 / 值"))
        self.power_label_1_5.setText(_translate("MainWindow", "兵种1 / 值"))
        self.power_label_1_6.setText(_translate("MainWindow", "兵种2 / 值"))
        self.power_2.setText(_translate("MainWindow", "<html><head/><body><p>专属</p></body></html>"))
        self.power_3.setText(_translate("MainWindow", "<html><head/><body><p>套装</p></body></html>"))
        item = self.listview_var_1.horizontalHeaderItem(0)
        item.setText(_translate("MainWindow", "布尔"))
        item = self.listview_var_1.horizontalHeaderItem(1)
        item.setText(_translate("MainWindow", "值"))
        item = self.listview_var_1.horizontalHeaderItem(2)
        item.setText(_translate("MainWindow", "地址"))
        item = self.listview_var_2.horizontalHeaderItem(0)
        item.setText(_translate("MainWindow", "整型"))
        item = self.listview_var_2.horizontalHeaderItem(1)
        item.setText(_translate("MainWindow", "值"))
        item = self.listview_var_2.horizontalHeaderItem(2)
        item.setText(_translate("MainWindow", "地址"))
        item = self.listview_var_3.horizontalHeaderItem(0)
        item.setText(_translate("MainWindow", "指针"))
        item = self.listview_var_3.horizontalHeaderItem(1)
        item.setText(_translate("MainWindow", "值"))
        item = self.listview_var_3.horizontalHeaderItem(2)
        item.setText(_translate("MainWindow", "地址"))
        self.var_input_4.setText(_translate("MainWindow", "跳转"))
        self.var_input_5.setText(_translate("MainWindow", "跳转"))
        self.var_input_6.setText(_translate("MainWindow", "跳转"))
        self.var_save_1.setText(_translate("MainWindow", "保存"))
        self.var_save_2.setText(_translate("MainWindow", "保存"))
        self.var_save_3.setText(_translate("MainWindow", "保存"))
        item = self.listview_diy.horizontalHeaderItem(0)
        item.setText(_translate("MainWindow", "变量名"))
        item = self.listview_diy.horizontalHeaderItem(1)
        item.setText(_translate("MainWindow", "值"))
        item = self.listview_diy.horizontalHeaderItem(2)
        item.setText(_translate("MainWindow", "值解释"))
        item = self.listview_diy.horizontalHeaderItem(3)
        item.setText(_translate("MainWindow", "地址"))
        item = self.listview_diy.horizontalHeaderItem(4)
        item.setText(_translate("MainWindow", "字节数"))
        item = self.listview_diy.horizontalHeaderItem(5)
        item.setText(_translate("MainWindow", "注释"))
        self.diy_input_2.setText(_translate("MainWindow", "保存值"))
        self.item_save_4.setText(_translate("MainWindow", "保存"))
        self.mk_1_1.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">武将1</span></p></body></html>"))
        self.mk_1_2.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">领悟等级</span></p></body></html>"))
        self.mk_1_4.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">领悟等级</span></p></body></html>"))
        self.mk_1_3.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">武将2</span></p></body></html>"))
        self.mk_1_5.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">武将3</span></p></body></html>"))
        self.mk_1_6.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">领悟等级</span></p></body></html>"))
        self.mk_1_8.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">领悟等级</span></p></body></html>"))
        self.mk_1_7.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">武将4</span></p></body></html>"))
        self.mk_1_10.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">领悟等级</span></p></body></html>"))
        self.mk_1_9.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">武将5</span></p></body></html>"))
        self.mk_1_11.setText(_translate("MainWindow", "<html><head/><body><p><span style=\" font-size:10pt;\">效果值</span></p></body></html>"))
        self.label_title.setText(_translate("MainWindow", "控制"))
        self.label_window1.setText(_translate("MainWindow", "窗体名称"))
        self.label_window2.setText(_translate("MainWindow", "进程说明"))
        self.label_help.setText(_translate("MainWindow", "将左方的\"准心图案\"拖拽至游戏中即可开始调试。"))
        self.version_0.setText(_translate("MainWindow", "6.4"))
        self.version_1.setText(_translate("MainWindow", "自动"))
        self.version_5.setText(_translate("MainWindow", "6.6"))
        self.version_6.setText(_translate("MainWindow", "6.5"))
        self.version_2.setText(_translate("MainWindow", "6.3"))
        self.version_3.setText(_translate("MainWindow", "6.2"))
        self.version_4.setText(_translate("MainWindow", "6.1"))
        self.action_C.setText(_translate("MainWindow", "控制(C)"))
        self.action_M.setText(_translate("MainWindow", "人物(M)"))
        self.action_B.setText(_translate("MainWindow", "战场(B)"))
        self.action_W.setText(_translate("MainWindow", "仓库(W)"))
        self.action_T.setText(_translate("MainWindow", "天赋(T)"))
        self.action_V.setText(_translate("MainWindow", "变量(V)"))
        self.action_D.setText(_translate("MainWindow", "自定义(D)"))
        self.action_K.setText(_translate("MainWindow", "必杀(K)"))
        self.war_input_47.setText("0")
        self.war_input_48.setText("0")
        self.war_input_49.setText("255")
        self.war_input_50.setText("255")
        self.war_input_51.setText("0")
        self.war_input_52.setText("0")
        self.war_input_53.setText("0")
        self.war_input_54.setText("0")

    #这里记录一些乱七八糟的变量和信号槽链接
    def otherSettings(self, MainWindow):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("myappid")
        source_dir = Path(__file__).resolve().parent
        self.base_dir = (Path(sys.executable).resolve().parent
                         if getattr(sys, "frozen", False) else source_dir)
        self.resource_dir = Path(getattr(sys, "_MEIPASS", source_dir))
        icon_path = self.resource_dir / "logo.ico"
        if icon_path.exists():
            self.setWindowIcon(QIcon(str(icon_path)))
        self.mouse_capture = False
        # 防止解绑过程中 Qt 的 selectionChanged 信号重入业务槽函数。
        self._detaching = False
        self._attach_in_progress = False
        self._cursor_handle = 0
        self.process = NULL
        self.p = NULL
        self.md = NULL
        self.memory_session = None
        self.engine_profile = None
        self._warehouse_snapshot = None
        self._item_global_snapshot = None
        self._consumable_snapshots = {}
        self._effect_snapshot = None
        self._exclusive_snapshot = None
        self._shown_exclusive_state = None
        self._fatal_snapshot = None
        self._variable_byte_snapshots = {}
        # (battle slot, Data ID) -> monotonic timestamp.  This is scoped to
        # one attached process and prevents the 1-second timer from submitting
        # the same dead unit repeatedly while the game is still rebuilding it.
        self._auto_revive_attempts = {}
        self.ok = False
        #窗口初始化
        self.widget_0.hide()
        self.widget_1.show()
        self.widget_1.setEnabled(False)
        self.widget_2.hide()
        self.widget_3.hide()
        self.widget_4.hide()
        self.widget_5.hide()
        self.widget_6.hide()
        self.widget_7.hide()
        self.widget_8.hide()
        pivot_path = self.resource_dir / "准星.png"
        if not pivot_path.exists():
            pivot_path = self.resource_dir / "准星.cur"
        pix = QtGui.QPixmap(str(pivot_path))
        self.label_pivot.setPixmap(pix)
        self.setFocusPolicy(QtCore.Qt.ClickFocus)
        self.setContextMenuPolicy(QtCore.Qt.NoContextMenu)

        #页面控件链接
        self.checkBox_1.clicked.connect(lambda: self.onButtonClick(1))
        self.checkBox_2.clicked.connect(lambda: self.onButtonClick(2))
        self.checkBox_3.clicked.connect(lambda: self.onButtonClick(3))
        self.checkBox_4.clicked.connect(lambda: self.onButtonClick(4))
        self.checkBox_5.clicked.connect(lambda: self.onButtonClick(5))
        self.checkBox_6.clicked.connect(lambda: self.onButtonClick(6))
        self.checkBox_7.clicked.connect(lambda: self.onButtonClick(7))
        self.checkBox_8.clicked.connect(lambda: self.onButtonClick(8))
        self.checkBox_9.clicked.connect(lambda: self.onButtonClick(9))

        self.listview_data.itemSelectionChanged.connect(lambda: self.onData())
        self.data_save.clicked.connect(lambda: self.saveData())
        self.data_recal.clicked.connect(lambda: self.recal())

        self.war_kind_1.clicked.connect(lambda: self.onWar(0))
        self.war_kind_2.clicked.connect(lambda: self.onWar(0))
        self.war_kind_3.clicked.connect(lambda: self.onWar(0))
        self.listview_war.itemSelectionChanged.connect(lambda: self.onWar(1))
        self.war_input_11.currentIndexChanged.connect(lambda: self.refreshFangzhen())
        self.war_connect.clicked.connect(lambda: self.onConnect())
        self.war_kill.clicked.connect(lambda: self.onKill())
        self.war_life.clicked.connect(lambda: self.onLife())
        self.war_save.clicked.connect(lambda: self.saveWar())
        self.war_lockadd.clicked.connect(lambda: self.onLock())
        self.war_locksub.clicked.connect(lambda: self.offLock())

        self.listview_item.itemSelectionChanged.connect(lambda: self.onItem())
        self.listview_item_2.itemSelectionChanged.connect(lambda: self.onItem())
        self.item_save.clicked.connect(lambda: self.saveItem())
        self.item_all_1.clicked.connect(lambda: self.allItem(1))
        self.item_all_2.clicked.connect(lambda: self.allItem(2))
        self.item_all_3.clicked.connect(lambda: self.allItem(3))

        self.listview_item_3.itemSelectionChanged.connect(lambda: self.onPower())
        self.item_save_2.clicked.connect(lambda: self.savePower())

        self.var_input_1.setText("0")
        self.var_input_2.setText("0")
        self.var_input_3.setText("0")
        self.listview_var_1.setColumnWidth(0,60)
        self.listview_var_1.setColumnWidth(1,55)
        self.listview_var_2.setColumnWidth(0,45)
        self.listview_var_2.setColumnWidth(1,75)
        self.listview_var_2.setColumnWidth(2,65)
        self.listview_var_3.setColumnWidth(0,45)
        self.listview_var_3.setColumnWidth(1,75)
        self.var_input_4.clicked.connect(lambda: self.onVar())
        self.var_input_5.clicked.connect(lambda: self.onVar())
        self.var_input_6.clicked.connect(lambda: self.onVar())
        self.var_save_1.clicked.connect(lambda: self.saveVar(1))
        self.var_save_2.clicked.connect(lambda: self.saveVar(2))
        self.var_save_3.clicked.connect(lambda: self.saveVar(3))

        self.listview_diy.setColumnWidth(0,100)
        self.listview_diy.setColumnWidth(1,80)
        self.listview_diy.setColumnWidth(2,90)
        self.listview_diy.setColumnWidth(3,70)
        self.listview_diy.setColumnWidth(4,45)
        self.listview_diy.setColumnWidth(5,230)
        self.diy_input_2.clicked.connect(lambda: self.saveDIY())
        self.diy_input_4.currentIndexChanged.connect(lambda: self.DIY_Page(1))

        self.listview_mk.itemSelectionChanged.connect(lambda: self.onMk())
        self.item_save_4.clicked.connect(lambda: self.saveMk())

        #控件属性设置
        self.my_thread = NULL
        self.lock_timer = QtCore.QTimer(MainWindow)
        self.lock_timer.setInterval(1000)
        self.lock_timer.timeout.connect(self.LockThread)
        self.item_input_1.setMaxVisibleItems(32)
        self.version_1.setChecked(True)
        self._set_effect_layout(False)

    def _set_status(self, text):
        visible = self.label_window2.fontMetrics().elidedText(
            text, QtCore.Qt.ElideRight, self.label_window2.width()-8)
        self.label_window2.setText(visible)
        self.label_window2.setToolTip(text)

    @staticmethod
    def _load_cursor_file(path):
        """Load a cursor with the Unicode Win32 API (LoadImageW).

        win32gui.LoadImage may route through the ANSI API on some pywin32
        builds, which cannot resolve a path containing Chinese characters.
        """
        user32 = ctypes.WinDLL("user32", use_last_error=True)
        user32.LoadImageW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p,
                                      ctypes.c_uint, ctypes.c_int,
                                      ctypes.c_int, ctypes.c_uint]
        user32.LoadImageW.restype = ctypes.c_void_p
        handle = user32.LoadImageW(None, str(Path(path)),
                                   win32con.IMAGE_CURSOR, 0, 0,
                                   win32con.LR_LOADFROMFILE)
        if not handle:
            raise OSError("无法加载准星文件: %s (WinError=%d)" %
                          (path, ctypes.get_last_error()))
        return int(handle)

    def _resolve_engine_version(self, version_part):
        if self.version_5.isChecked():
            return int(EngineVersion.V66)
        if self.version_6.isChecked():
            return int(EngineVersion.V65)
        if self.version_0.isChecked():
            return int(EngineVersion.V64)
        if self.version_2.isChecked():
            return int(EngineVersion.V63)
        if self.version_3.isChecked():
            return int(EngineVersion.V62)
        if self.version_4.isChecked():
            return int(EngineVersion.V61)
        return int({
            6: EngineVersion.V66,
            5: EngineVersion.V65,
            4: EngineVersion.V64,
            3: EngineVersion.V63,
            2: EngineVersion.V62,
        }.get(int(version_part), EngineVersion.V61))

    def _manual_engine_hint(self):
        if self.version_1.isChecked():
            return None
        return self._resolve_engine_version(1)

    def _read_text(self, address, size, encoding="gbk"):
        try:
            raw = self.memory_session.read_bytes(address, size).split(b"\0", 1)[0]
        except MemoryAccessError:
            return "不可读"
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            return "非法字符"

    def _populate_profile_effect_names(self):
        if self.engine_profile is None:
            raise ProfileError("尚未建立引擎画像")
        names = self.engine_profile.effect_name_catalog()
        ui_count = self.engine_profile.limits.get("effect_count", len(names))
        if not 0 <= ui_count <= len(names):
            raise ProfileError(
                "特效名称界面范围无效: %d/%d" % (ui_count, len(names)))
        self.listview_item_3.clear()
        for effect_id, name in enumerate(names[:ui_count]):
            self.listview_item_3.addItem("%d:%s" % (effect_id, name))

    def _parse_uint(self, widget, bits, label):
        text = widget.toPlainText().strip()
        if text == "":
            raise ValueError(label + "不能为空")
        value = int(text, 10)
        if not 0 <= value < (1 << bits):
            raise ValueError("%s超出 UInt%d 范围" % (label, bits))
        return value

    def _profile_address(self, key, fallback):
        if self.engine_profile is None:
            return fallback
        return int(self.engine_profile.addresses.get(key, fallback))

    def _variable_layout(self, capability_key):
        layouts = (self.engine_profile.metadata.get("variable_layouts", {})
                   if self.engine_profile is not None else {})
        layout = layouts.get(capability_key)
        if isinstance(layout, dict):
            return layout
        address_keys = {
            "variables_bool": "bool_vars",
            "variables_int": "int_vars",
            "variables_ptr": "ptr_vars",
        }
        return {
            "addressKey": address_keys[capability_key],
            "dataOffset": 0, "stride": 4, "width": 4, "signed": True,
        }

    def _variable_address(self, capability_key, index):
        layout = self._variable_layout(capability_key)
        base = self._profile_address(
            layout["addressKey"], {
                "variables_bool": 0x00492FC8,
                "variables_int": 0x00502000,
                "variables_ptr": 0x00506000,
            }[capability_key])
        if layout.get("storage") == "bitset":
            return (base + int(layout.get("dataOffset", 0)) +
                    int(index) // 8)
        return (base + int(layout.get("dataOffset", 0)) +
                int(index) * int(layout["stride"]))

    @staticmethod
    def _variable_bit_mask(layout, index):
        bit_index = int(index) % 8
        if layout.get("bitOrder") == "msb0":
            return 0x80 >> bit_index
        return 1 << bit_index

    def _set_effect_layout(self, is_66):
        labels = (self.power_label_1_2, self.power_label_1_3,
                  self.power_label_1_4, self.power_label_1_5,
                  self.power_label_1_6)
        extended = labels + (
            self.power_input_1_6, self.power_input_1_7,
            self.power_input_1_8, self.power_input_1_9,
            self.power_input_1_10, self.power_input_1_11,
            self.power_input_1_12)
        for control in extended:
            control.setVisible(is_66)
        if is_66:
            self.power_1.setGeometry(QtCore.QRect(185, 15, 136, 20))
            self.power_1.setText("天赋·角色1 / 值")
            for control, x, y in (
                    (self.power_input_1_1, 185, 40),
                    (self.power_input_1_2, 355, 40),
                    (self.power_input_1_3, 525, 40),
                    (self.power_input_1_4, 185, 80),
                    (self.power_input_1_9, 355, 80),
                    (self.power_input_1_11, 525, 80)):
                control.setGeometry(QtCore.QRect(x, y, 96, 25))
            for control, x, y in (
                    (self.power_input_1_5, 285, 40),
                    (self.power_input_1_6, 455, 40),
                    (self.power_input_1_7, 625, 40),
                    (self.power_input_1_8, 285, 80),
                    (self.power_input_1_10, 455, 80),
                    (self.power_input_1_12, 625, 80)):
                control.setGeometry(QtCore.QRect(x, y, 36, 25))
            return
        self.power_1.setGeometry(QtCore.QRect(185, 15, 31, 20))
        self.power_1.setText("<html><head/><body><p>天赋</p></body></html>")
        self.power_input_1_1.setGeometry(QtCore.QRect(185, 40, 136, 25))
        self.power_input_1_2.setGeometry(QtCore.QRect(355, 40, 136, 25))
        self.power_input_1_3.setGeometry(QtCore.QRect(525, 40, 136, 25))
        self.power_input_1_4.setGeometry(QtCore.QRect(185, 80, 136, 25))
        self.power_input_1_5.setGeometry(QtCore.QRect(355, 80, 136, 25))

    def _selected_war_slot(self):
        item = self.listview_war.currentItem()
        if item is None:
            return -1
        slot = item.data(QtCore.Qt.UserRole)
        if slot is not None:
            return int(slot)
        row = self.listview_war.currentRow()
        if self.war_kind_2.isChecked():
            return row + cnt_wo
        if self.war_kind_3.isChecked():
            return row + cnt_wo + cnt_you
        return row

    def _variable_values(self, capability_key, start):
        if not self._feature_readable(capability_key):
            return []
        count = min(start+100, 4096)-start
        layout = self._variable_layout(capability_key)
        if layout.get("storage") == "bitset":
            if count <= 0:
                return []
            first_byte = start // 8
            last_byte = (start + count - 1) // 8
            base = self._profile_address(layout["addressKey"], 0x00492FC8)
            raw = self.memory_session.read_bytes(
                base + int(layout.get("dataOffset", 0)) + first_byte,
                last_byte - first_byte + 1)
            snapshots = getattr(self, "_variable_byte_snapshots", {})
            for offset, value in enumerate(raw):
                snapshots[(capability_key,
                           base + int(layout.get("dataOffset", 0)) +
                           first_byte + offset)] = bytes((value,))
            self._variable_byte_snapshots = snapshots
            return [1 if raw[(start + offset) // 8 - first_byte] &
                    self._variable_bit_mask(layout, start + offset) else 0
                    for offset in range(count)]
        width = int(layout["width"])
        stride = int(layout["stride"])
        address = self._variable_address(capability_key, start)
        raw_size = 0 if count == 0 else (count - 1) * stride + width
        raw = self.memory_session.read_bytes(address, raw_size)
        snapshots = getattr(self, "_variable_byte_snapshots", {})
        for index in range(count):
            item_address = address + index * stride
            snapshots[(capability_key, item_address)] = bytes(
                raw[index*stride:index*stride+width])
        self._variable_byte_snapshots = snapshots
        return [int.from_bytes(raw[index*stride:index*stride+width], "little",
                               signed=bool(layout.get("signed", False)))
                for index in range(count)]

    def _feature_enabled(self, key):
        return self._feature_writable(key)

    def _feature_readable(self, key):
        return (not self._detaching and self.memory_session is not None and
                self.engine_profile is not None and
                self.engine_profile.can_read(key))

    def _feature_writable(self, key):
        return (not self._detaching and self.memory_session is not None and
                self.engine_profile is not None and
                self.engine_profile.can_write(key))

    def _feature_tooltip(self, key):
        """Return a risk warning without turning direct compatibility into a gate."""
        if self.engine_profile is None:
            return ""
        warning = getattr(self.engine_profile, "compatibility_warning", None)
        if callable(warning):
            return warning(key)
        return ""

    def _consumable_mapping(self):
        if not self._feature_readable("item_consumables"):
            return None
        mapping = self.engine_profile.metadata.get("consumable_mapping")
        if not isinstance(mapping, dict):
            return None
        return mapping

    def _item_ids(self, domain="record"):
        """Return the profile's validated metadata/equipment/item domain."""
        if self.engine_profile is None:
            return []
        try:
            return list(self.engine_profile.item_ids(domain))
        except (AttributeError, ValueError):
            count = int(self.engine_profile.limits.get("item_count", 0))
            if domain in ("equipment", "equip"):
                count = int(self.engine_profile.limits.get(
                    "equipment_count", count))
                return list(range(max(0, min(0xFF, count))))
            return list(range(max(0, min(0xFF, count))))

    def _is_equipment_item(self, item_id):
        return int(item_id) in set(self._item_ids("equipment"))

    def _is_item_record(self, item_id):
        return int(item_id) in set(self._item_ids("record"))

    def _validate_equipment_selection(self, control, start, end, label):
        """Validate an equipment-slot combo before writing its byte token."""
        selected = int(control.currentIndex())
        if selected == cnt_item:
            return True
        if not self._is_item_record(selected) or not self._is_equipment_item(selected):
            self._set_status("%s cannot use an item/consumable ID" % label)
            return False
        if not start <= selected <= end:
            self._set_status("%s item ID is outside its equipment category" % label)
            return False
        return True

    def _consumable_ids(self):
        mapping = self._consumable_mapping()
        if mapping is None:
            values = self.engine_profile.metadata.get("consumable_item_ids", [])
            return [int(item) for item in values]
        explicit = [int(item) for item in mapping.get("itemIds", [])]
        if explicit:
            return explicit
        start = int(mapping["idMin"])
        end = int(mapping["idMax"])
        pairs = {tuple(int(value) for value in item)
                 for item in mapping.get("categoryPairs", [])}
        if not pairs:
            return list(range(start, end + 1))
        table = self.engine_profile.item_table_66()
        result = []
        for item_id in range(start, end + 1):
            offset = item_id * 0x19
            if (table[offset + 0x11], table[offset + 0x12]) in pairs:
                result.append(item_id)
        return result

    def _consumable_base(self):
        mapping = self._consumable_mapping()
        if mapping is None:
            raise MemoryAccessError("6.6消耗品计数映射当前不可用")
        base = self.engine_profile.addresses["consumable_counts"]
        if mapping.get("baseDereference"):
            base = self.memory_session.read_u32(base)
        return base

    def _consumable_address(self, item_id):
        mapping = self._consumable_mapping()
        if mapping is None:
            raise MemoryAccessError("6.6消耗品计数映射当前不可用")
        item_id = int(item_id)
        explicit_ids = [int(item) for item in mapping.get("itemIds", [])]
        explicit_addresses = [int(item) for item in mapping.get("addresses", [])]
        if explicit_addresses:
            if len(explicit_ids) != len(explicit_addresses):
                raise MemoryAccessError("强制消耗品 ID 与地址数量不一致")
            try:
                return explicit_addresses[explicit_ids.index(item_id)]
            except ValueError as exc:
                raise MemoryAccessError("消耗品 ID 不在强制映射中") from exc
        if not int(mapping["idMin"]) <= item_id <= int(mapping["idMax"]):
            raise MemoryAccessError("消耗品 ID 超出已验证映射范围")
        return (self._consumable_base() +
                (item_id - int(mapping["idMin"])) * int(mapping["stride"]))

    def _read_consumable(self, item_id):
        mapping = self._consumable_mapping()
        address = self._consumable_address(item_id)
        width = int(mapping["width"])
        raw = self.memory_session.read_bytes(address, width)
        snapshots = getattr(self, "_consumable_snapshots", {})
        snapshots[address] = raw
        self._consumable_snapshots = snapshots
        return int.from_bytes(raw, "little", signed=False)

    def _consumable_entry(self, item_id, value):
        mapping = self._consumable_mapping()
        width = int(mapping["width"])
        if not 0 <= int(value) < (1 << (width * 8)):
            raise ValueError("消耗品数量超出 UInt%d 范围" % (width * 8))
        return self._consumable_address(item_id), int(value).to_bytes(
            width, "little", signed=False)

    def _reject_feature(self, control, key):
        capability = self.engine_profile.capability(key)
        control.setChecked(False)
        control.setToolTip(capability.reason)
        self._set_status(capability.reason)

    def _forced_mode(self):
        return bool(self.engine_profile is not None and
                    getattr(self.engine_profile, "direct_compatibility", False))

    def _forced_legacy_patch(self, key):
        # 6.6 只允许已解析的完整指令配方，不回退到旧版固定地址。
        return None

    def _apply_capabilities(self):
        if self.engine_profile is None:
            return
        mapping = {
            self.checkBox_1: "control_friendly",
            self.checkBox_2: "control_all_factions",
            self.checkBox_3: "control_no_ai",
            self.checkBox_4: "control_move_after_wait",
            self.checkBox_5: "control_move_255",
            self.checkBox_6: "control_pass_through",
            self.checkBox_7: "control_auto_return",
            self.checkBox_8: "battle",
            self.checkBox_9: "revive",
            self.data_save: "person",
            self.data_recal: "recalculate",
            self.war_save: "battle",
            self.war_kill: "battle",
            self.war_life: "revive",
            self.item_all_1: "item_bulk_treasure",
            self.item_all_2: "item_consumables",
            self.item_save_2: "effect",
            self.var_save_1: "variables_bool",
            self.var_save_2: "variables_int",
            self.var_save_3: "variables_ptr",
            self.diy_input_2: "diy",
            self.item_save_4: "fatal",
        }
        for control, key in mapping.items():
            capability = self.engine_profile.capability(key)
            usable = self.engine_profile.can_write(key)
            control.setEnabled(usable)
            resolved = self.engine_profile.resolved_feature(key)
            if usable:
                control.setToolTip(self._feature_tooltip(key))
            else:
                control.setToolTip(resolved.reason or capability.reason)
        item_save_usable = (self.engine_profile.can_write("warehouse") or
                            self.engine_profile.can_write("item_consumables"))
        self.item_save.setEnabled(item_save_usable)
        if not item_save_usable:
            self.item_save.setToolTip("仓库与消耗品当前均不可写")
        clear_usable = (self.engine_profile.can_write("warehouse") and
                        self.engine_profile.can_write("item_consumables"))
        self.item_all_3.setEnabled(clear_usable)
        if not clear_usable:
            self.item_all_3.setToolTip("清空需要仓库与消耗品同时可写")
        effect_save = (self.engine_profile.can_write("effect") and
                       self.engine_profile.can_write("effect_exclusive"))
        self.item_save_2.setEnabled(effect_save)
        if not effect_save:
            reasons = [self.engine_profile.resolved_feature(key).reason
                       for key in ("effect", "effect_exclusive")
                       if not self.engine_profile.can_write(key)]
            self.item_save_2.setToolTip("；".join(item for item in reasons if item))
        direction_capability = self.engine_profile.capability(
            "turn_refresh_direction")
        direction_warning = self._feature_tooltip("turn_refresh_direction")
        self.war_input_16.setToolTip(
            direction_warning if direction_warning else
            ("使用已定位的转向入口" if
             self.engine_profile.can_write("turn_refresh_direction") else
             direction_capability.reason + "；当前仅保存方向字段"))
        self.war_input_9.setToolTip("")
        self.war_input_10.setToolTip("")

    def _sync_66_patch_controls(self):
        if self.engine_profile is None or not self.engine_profile.is_66:
            return
        controls = {
            "control_friendly": self.checkBox_1,
            "control_all_factions": self.checkBox_2,
            "control_no_ai": self.checkBox_3,
            "control_move_after_wait": self.checkBox_4,
            "control_move_255": self.checkBox_5,
            "control_pass_through": self.checkBox_6,
            "control_auto_return": self.checkBox_7,
        }
        for key, control in controls.items():
            patch = self.engine_profile.patches.get(key)
            dynamic_patch = self.engine_profile.dynamic_patches.get(key)
            checked = False
            if patch is not None:
                try:
                    states = [self.memory_session.read_bytes(
                        site.address, len(site.patched)) for site in patch.sites]
                    checked = all(current == site.patched
                                  for site, current in zip(patch.sites, states))
                    original = all(current == site.original
                                   for site, current in zip(patch.sites, states))
                    if not checked and not original:
                        reason = "%s存在部分补丁站点，拒绝接管" % patch.label
                        control.setToolTip(reason)
                        self._set_status(reason)
                except MemoryAccessError:
                    checked = False
            elif dynamic_patch is not None:
                checked = self.memory_session.is_dynamic_patch_enabled(key)
            control.blockSignals(True)
            control.setChecked(checked)
            control.blockSignals(False)

    def _detach_process(self):
        global auto_life
        global version
        global addr_tianfu
        global addr_bisha
        global cnt_item
        if self._detaching:
            return
        self._detaching = True
        auto_life = False
        version = int(EngineVersion.V63_MP)
        if hasattr(self, "lock_timer"):
            self.lock_timer.stop()
        # 先停止定时器并使业务槽函数失效，再关闭句柄；否则清空列表会
        # 通过 itemSelectionChanged 重入 onItem/onPower，访问已关闭会话。
        if self.memory_session is not None:
            try:
                self.memory_session.close()
            except Exception as exc:
                self._set_status("关闭游戏会话失败: %s" % exc)
        self.memory_session = None
        self.engine_profile = None
        self.md = NULL
        self.p = NULL
        self.ok = False
        addr_tianfu = 0
        addr_bisha = 0
        cnt_item = 0
        self._warehouse_snapshot = None
        self._item_global_snapshot = None
        self._consumable_snapshots = {}
        self._effect_snapshot = None
        self._exclusive_snapshot = None
        self._shown_exclusive_state = None
        self._fatal_snapshot = None
        self._variable_byte_snapshots = {}
        self._auto_revive_attempts = {}
        controls_to_clear = []
        for name in ("listview_data", "listview_war", "listview_war_2",
                     "listview_item", "listview_item_2", "listview_item_3",
                     "listview_mk"):
            control = getattr(self, name, None)
            if control is not None:
                controls_to_clear.append(control)
                control.blockSignals(True)
                control.clear()
        for name in (
                "item_input_1", "data_input_32", "data_input_35",
                "data_input_38", "power_input_1_1", "power_input_1_2",
                "power_input_1_3", "power_input_1_4", "power_input_1_9",
                "power_input_1_11"):
            control = getattr(self, name, None)
            if control is not None:
                control.clear()
        if hasattr(self, "power_input_1_12"):
            self._set_effect_layout(False)
        lock_list.clear()
        lock_hp.clear()
        lock_mp.clear()
        lock_ids.clear()
        for control in controls_to_clear:
            control.blockSignals(False)
        self._detaching = False

    def closeEvent(self, event):
        self._detach_process()
        event.accept()
    
    #打开第一页
    def on_action_C_triggered(self):
        self.widget_1.show()
        self.widget_2.hide()
        self.widget_3.hide()
        self.widget_4.hide()
        self.widget_5.hide()
        self.widget_6.hide()
        self.widget_7.hide()
        self.widget_8.hide()
        self.label_title.setText("控制")

    #打开第二页
    def on_action_M_triggered(self):
        self.widget_1.hide()
        self.widget_2.show()
        self.widget_3.hide()
        self.widget_4.hide()
        self.widget_5.hide()
        self.widget_6.hide()
        self.widget_7.hide()
        self.widget_8.hide()
        self.label_title.setText("人物")

    #打开第三页
    def on_action_B_triggered(self):
        self.widget_1.hide()
        self.widget_2.hide()
        self.widget_3.show()
        self.widget_4.hide()
        self.widget_5.hide()
        self.widget_6.hide()
        self.widget_7.hide()
        self.widget_8.hide()
        self.label_title.setText("战场")

    #打开第四页
    def on_action_W_triggered(self):
        self.widget_1.hide()
        self.widget_2.hide()
        self.widget_3.hide()
        self.widget_4.show()
        self.widget_5.hide()
        self.widget_6.hide()
        self.widget_7.hide()
        self.widget_8.hide()
        self.label_title.setText("仓库")

    #打开第五页
    def on_action_T_triggered(self):
        global version
        if version == -1:
            return
        self.widget_1.hide()
        self.widget_2.hide()
        self.widget_3.hide()
        self.widget_4.hide()
        self.widget_5.show()
        self.widget_6.hide()
        self.widget_7.hide()
        self.widget_8.hide()
        self.label_title.setText("天赋")

    #打开第六页
    def on_action_V_triggered(self):
        global version
        if version == -1:
            return
        self.widget_1.hide()
        self.widget_2.hide()
        self.widget_3.hide()
        self.widget_4.hide()
        self.widget_5.hide()
        self.widget_6.show()
        self.widget_7.hide()
        self.widget_8.hide()
        self.label_title.setText("变量")

    #打开第七页
    def on_action_D_triggered(self):
        global version
        if version == -1:
            return
        self.widget_1.hide()
        self.widget_2.hide()
        self.widget_3.hide()
        self.widget_4.hide()
        self.widget_5.hide()
        self.widget_6.hide()
        self.widget_7.show()
        self.widget_8.hide()
        self.label_title.setText("自定")

    #打开第八页
    def on_action_K_triggered(self):
        global version
        if version == -1:
            return
        self.widget_1.hide()
        self.widget_2.hide()
        self.widget_3.hide()
        self.widget_4.hide()
        self.widget_5.hide()
        self.widget_6.hide()
        self.widget_7.hide()
        self.widget_8.show()
        self.label_title.setText("必杀")

    #扳手第一页功能实现
    def onButtonClick(self, n):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        global auto_life
        if version == 6 and n <= 7:
            keys = {
                1: "control_friendly",
                2: "control_all_factions",
                3: "control_no_ai",
                4: "control_move_after_wait",
                5: "control_move_255",
                6: "control_pass_through",
                7: "control_auto_return",
            }
            controls = {
                1: self.checkBox_1, 2: self.checkBox_2, 3: self.checkBox_3,
                4: self.checkBox_4, 5: self.checkBox_5, 6: self.checkBox_6,
                7: self.checkBox_7,
            }
            key = keys[n]
            control = controls[n]
            capability = self.engine_profile.capability(key)
            normal_patch = self.engine_profile.patches.get(key)
            dynamic_patch = self.engine_profile.dynamic_patches.get(key)
            if normal_patch is not None or dynamic_patch is not None:
                try:
                    if dynamic_patch is not None:
                        self.memory_session.set_dynamic_patch(
                            dynamic_patch, control.isChecked())
                    else:
                        self.memory_session.set_patch(
                            normal_patch, control.isChecked())
                    self._set_status(self.engine_profile.summary())
                except (MemoryAccessError, PatchError) as exc:
                    control.blockSignals(True)
                    control.setChecked(not control.isChecked())
                    control.blockSignals(False)
                    self._set_status(str(exc))
                return
            # Unknown derivatives fall through to the historic fixed-address
            # implementation instead of being disabled by capability evidence.
        if n == 1:
            if self.checkBox_1.isChecked() == True:
                self.md.WriteProcessMemory(int(self.p), 0x43D574, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43D5A5, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43E359, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E748, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E749, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E754, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E755, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x455379, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
            else:
                self.md.WriteProcessMemory(int(self.p), 0x43D574, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43D5A5, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43E359, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E748, ctypes.byref(ctypes.c_int(0x75)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E749, ctypes.byref(ctypes.c_int(0xC8)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E754, ctypes.byref(ctypes.c_int(0x75)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44E755, ctypes.byref(ctypes.c_int(0xBC)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x455379, ctypes.byref(ctypes.c_int(0x74)), 1, None)
        if n == 2:
            if self.checkBox_2.isChecked() == True:
                #self.md.WriteProcessMemory(int(self.p), 0x4242AF, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x4242B0, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                if version in (EngineVersion.V64, EngineVersion.V65):
                    self.md.WriteProcessMemory(int(self.p), 0x4242AF, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437BC0, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437BC1, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x454405, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x454406, ctypes.byref(ctypes.c_int(0x90)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x455646, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
            else:
                #self.md.WriteProcessMemory(int(self.p), 0x4242AF, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x4242B0, ctypes.byref(ctypes.c_int(0x32)), 1, None)
                if version in (EngineVersion.V64, EngineVersion.V65):
                    self.md.WriteProcessMemory(int(self.p), 0x4242AF, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x4242B0, ctypes.byref(ctypes.c_int(0xDD)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437BC0, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437BC1, ctypes.byref(ctypes.c_int(0x34)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x454405, ctypes.byref(ctypes.c_int(0x75)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x454406, ctypes.byref(ctypes.c_int(0x27)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x455646, ctypes.byref(ctypes.c_int(0x75)), 1, None)
        if n == 3:
            if self.checkBox_3.isChecked() == True:
                self.md.WriteProcessMemory(int(self.p), 0x44E9D3, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
            else:
                self.md.WriteProcessMemory(int(self.p), 0x44E9D3, ctypes.byref(ctypes.c_int(0x74)), 1, None)
        if n == 4:
            if self.checkBox_4.isChecked() == True:
                self.md.WriteProcessMemory(int(self.p), 0x43D5DC, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
                #self.md.WriteProcessMemory(int(self.p), 0x470B8C, ctypes.byref(ctypes.c_int(0x42)), 1, None)
            else:
                self.md.WriteProcessMemory(int(self.p), 0x43D5DC, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                #self.md.WriteProcessMemory(int(self.p), 0x470B8C, ctypes.byref(ctypes.c_int(0x47)), 1, None)
        if n == 5:
            if self.checkBox_5.isChecked() == True:
                self.md.WriteProcessMemory(int(self.p), 0x43F74F, ctypes.byref(ctypes.c_int(0x33)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43F750, ctypes.byref(ctypes.c_int(0xC0)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43F751, ctypes.byref(ctypes.c_int(0x48)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43F752, ctypes.byref(ctypes.c_int(0xC3)), 1, None)
            else:
                self.md.WriteProcessMemory(int(self.p), 0x43F74F, ctypes.byref(ctypes.c_int(0x55)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43F750, ctypes.byref(ctypes.c_int(0x8B)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43F751, ctypes.byref(ctypes.c_int(0xEC)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x43F752, ctypes.byref(ctypes.c_int(0x83)), 1, None)
        if n == 6:
            if self.checkBox_6.isChecked() == True:
                self.md.WriteProcessMemory(int(self.p), 0x4387B7, ctypes.byref(ctypes.c_int(0xEB)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437D59, ctypes.byref(ctypes.c_int(0x33)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437D5A, ctypes.byref(ctypes.c_int(0xC0)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437D5B, ctypes.byref(ctypes.c_int(0xC3)), 1, None)
            else:
                self.md.WriteProcessMemory(int(self.p), 0x4387B7, ctypes.byref(ctypes.c_int(0x74)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437D59, ctypes.byref(ctypes.c_int(0x55)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437D5A, ctypes.byref(ctypes.c_int(0x8B)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x437D5B, ctypes.byref(ctypes.c_int(0xEC)), 1, None)
        if n == 7:
            if self.checkBox_7.isChecked() == True:
                data = ctypes.c_int()
                self.md.ReadProcessMemory(int(self.p), 0x44E570, ctypes.byref(data), 1, None)
                if data.value == 0xE8:
                    self.md.WriteProcessMemory(int(self.p), 0x44E571, ctypes.byref(ctypes.c_int(0x8B)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E572, ctypes.byref(ctypes.c_int(0x78)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E573, ctypes.byref(ctypes.c_int(0x03)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E574, ctypes.byref(ctypes.c_int(0x00)), 1, None)
                else:
                    self.md.WriteProcessMemory(int(self.p), 0x44E576, ctypes.byref(ctypes.c_int(0x86)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E577, ctypes.byref(ctypes.c_int(0x78)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E578, ctypes.byref(ctypes.c_int(0x03)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E579, ctypes.byref(ctypes.c_int(0x00)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44EA74, ctypes.byref(ctypes.c_int(0x88)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44EA75, ctypes.byref(ctypes.c_int(0x73)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44EA76, ctypes.byref(ctypes.c_int(0x03)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x44EA77, ctypes.byref(ctypes.c_int(0x00)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E00, ctypes.byref(ctypes.c_long(0x83EC8B55)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E04, ctypes.byref(ctypes.c_long(0x75FF04EC)), 4, None)
                if version in (EngineVersion.V62, EngineVersion.V61):
                    self.md.WriteProcessMemory(int(self.p), 0x485E08, ctypes.byref(ctypes.c_long(0x9E8DE808)), 4, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E0C, ctypes.byref(ctypes.c_long(0x4D8B0004)), 4, None)
                else:
                    self.md.WriteProcessMemory(int(self.p), 0x485E08, ctypes.byref(ctypes.c_long(0xCF44E808)), 4, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E0C, ctypes.byref(ctypes.c_long(0x4D8BFFF8)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E10, ctypes.byref(ctypes.c_long(0xFFE18108)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E17, ctypes.byref(ctypes.c_long(0xC96B)), 2, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E19, ctypes.byref(ctypes.c_long(len_war)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E1A, ctypes.byref(ctypes.c_long(0x81)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E1B, ctypes.byref(ctypes.c_long(0xC1)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E1C, ctypes.byref(ctypes.c_long(addr_war)), 3, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E20, ctypes.byref(ctypes.c_long(0xB8FC4D89)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E24, ctypes.byref(ctypes.c_int(0x06)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E28, ctypes.byref(ctypes.c_long(0x4120D0F7)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E2C, ctypes.byref(ctypes.c_long(0x0D)), 1, None)
                if version in (EngineVersion.V63_MP, EngineVersion.V63,
                                EngineVersion.V62, EngineVersion.V61):
                    self.md.WriteProcessMemory(int(self.p), 0x485E2D, ctypes.byref(ctypes.c_long(0xE831FF)), 3, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E30, ctypes.byref(ctypes.c_long(0xFFFB86A2)), 4, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E34, ctypes.byref(ctypes.c_long(0x00008868)), 4, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E38, ctypes.byref(ctypes.c_long(0x50066A00)), 4, None)
                else:
                    self.md.WriteProcessMemory(int(self.p), 0x485E2D, ctypes.byref(ctypes.c_long(0x00008868)), 4, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E31, ctypes.byref(ctypes.c_long(0x8B066A00)), 4, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E35, ctypes.byref(ctypes.c_long(0x71FFFC4D)), 4, None)
                    self.md.WriteProcessMemory(int(self.p), 0x485E39, ctypes.byref(ctypes.c_long(0x909004)), 3, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E3C, ctypes.byref(ctypes.c_long(0xE8)), 1, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E3D, ctypes.byref(ctypes.c_long(0xFFFB8869)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E41, ctypes.byref(ctypes.c_long(0xFE5DE58B)), 4, None)
                self.md.WriteProcessMemory(int(self.p), 0x485E45, ctypes.byref(ctypes.c_long(0x00C3FC4D)), 4, None)
            else:
                if version in (-1, EngineVersion.V64, EngineVersion.V65,
                               EngineVersion.V63_MP, EngineVersion.V63):
                    self.md.WriteProcessMemory(int(self.p), 0x44E571, ctypes.byref(ctypes.c_int(0xDD)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E572, ctypes.byref(ctypes.c_int(0x47)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E573, ctypes.byref(ctypes.c_int(0xFC)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44E574, ctypes.byref(ctypes.c_int(0xFF)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA74, ctypes.byref(ctypes.c_int(0xDA)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA75, ctypes.byref(ctypes.c_int(0x42)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA76, ctypes.byref(ctypes.c_int(0xFC)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA77, ctypes.byref(ctypes.c_int(0xFF)), 1, None)
                else:
                    data = ctypes.c_int()
                    self.md.ReadProcessMemory(int(self.p), 0x44E570, ctypes.byref(data), 1, None)
                    if data.value == 0xE8:
                        self.md.WriteProcessMemory(int(self.p), 0x44E571, ctypes.byref(ctypes.c_int(0x26)), 1, None)
                        self.md.WriteProcessMemory(int(self.p), 0x44E572, ctypes.byref(ctypes.c_int(0x17)), 1, None)
                        self.md.WriteProcessMemory(int(self.p), 0x44E573, ctypes.byref(ctypes.c_int(0x08)), 1, None)
                        self.md.WriteProcessMemory(int(self.p), 0x44E574, ctypes.byref(ctypes.c_int(0x00)), 1, None)
                    else:
                        self.md.WriteProcessMemory(int(self.p), 0x44E576, ctypes.byref(ctypes.c_int(0x21)), 1, None)
                        self.md.WriteProcessMemory(int(self.p), 0x44E577, ctypes.byref(ctypes.c_int(0x17)), 1, None)
                        self.md.WriteProcessMemory(int(self.p), 0x44E578, ctypes.byref(ctypes.c_int(0x08)), 1, None)
                        self.md.WriteProcessMemory(int(self.p), 0x44E579, ctypes.byref(ctypes.c_int(0x00)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA74, ctypes.byref(ctypes.c_int(0x23)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA75, ctypes.byref(ctypes.c_int(0x12)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA76, ctypes.byref(ctypes.c_int(0x08)), 1, None)
                    self.md.WriteProcessMemory(int(self.p), 0x44EA77, ctypes.byref(ctypes.c_int(0x00)), 1, None)
        if n == 8:
            if self.checkBox_8.isChecked() == True:
                for i in range(0,cnt_wo + cnt_you):
                    data = ctypes.c_int()
                    self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war, ctypes.byref(data), 2, None)
                    if data.value == 65535:
                        continue
                    self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0xE, ctypes.byref(ctypes.c_int(1)), 1, None)
            else:
                for i in range(0,cnt_wo + cnt_you):
                    data = ctypes.c_int()
                    self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war, ctypes.byref(data), 2, None)
                    if data.value == 65535:
                        continue
                    self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0xE, ctypes.byref(ctypes.c_int(7)), 1, None)
        '''if n == 9:
            if self.checkBox_9.isChecked() == True:
                self.md.WriteProcessMemory(int(self.p), 0x404388, ctypes.byref(ctypes.c_int(0xC3)), 1, None)
                if version in (EngineVersion.V64, EngineVersion.V65):
                    self.md.WriteProcessMemory(int(self.p), 0x4214A1, ctypes.byref(ctypes.c_int(0xC3)), 1, None)
                else:
                    self.md.WriteProcessMemory(int(self.p), 0x4214C9, ctypes.byref(ctypes.c_int(0xC3)), 1, None)
            else:
                self.md.WriteProcessMemory(int(self.p), 0x404388, ctypes.byref(ctypes.c_int(0x55)), 1, None)
                if version in (EngineVersion.V64, EngineVersion.V65):
                    self.md.WriteProcessMemory(int(self.p), 0x4214A1, ctypes.byref(ctypes.c_int(0x55)), 1, None)
                else:
                    self.md.WriteProcessMemory(int(self.p), 0x4214C9, ctypes.byref(ctypes.c_int(0xC3)), 1, None)'''
        if n == 9:
            auto_life = self.checkBox_9.isChecked()

    #扳手第二页人物data数据读入
    def onData(self):
        if self._detaching or self.memory_session is None or self.engine_profile is None:
            return
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        global cnt_item
        global version
        if version == 6 and not self._feature_readable("person"):
            self._set_status(self.engine_profile.capability("person").reason)
            return
        if len(self.listview_data.selectedIndexes())<=0:
            n = 0
        else:
            n = self.listview_data.selectedIndexes()[0].row()
        person_r = self._profile_address("person_r", 0x0050F800)
        person_s = self._profile_address("person_s", 0x00501000)
        person_face = self._profile_address("person_face", 0x0050F000)
        person_pointer = self._profile_address("person_pointer", 0x004CEA00)
        data = ctypes.c_int()
        #R形象
        self.md.ReadProcessMemory(int(self.p), person_r+n*2, ctypes.byref(data), 2, None)
        self.data_input_1.setText(str(data.value))
        #S形象
        self.md.ReadProcessMemory(int(self.p), person_s+n*2, ctypes.byref(data), 2, None)
        self.data_input_2.setText(str(data.value))
        #头像
        self.md.ReadProcessMemory(int(self.p), person_face+n*2, ctypes.byref(data), 2, None)
        self.data_input_3.setText(str(data.value))
        mem = ctypes.c_uint32()
        mem.value = self.memory_session.read_u32(person_pointer)
        if version == EngineVersion.V66:
            self.memory_session.clear_write_guards("person.")
            self._person_pointer_snapshot = mem.value
            self.memory_session.set_write_guard(
                "person.main", mem.value + 0x48 * n,
                self.memory_session.read_actual_bytes(
                    mem.value + 0x48 * n, 0x48), actual_address=True)
            for key, address in (
                    ("person.r", person_r + n * 2),
                    ("person.s", person_s + n * 2),
                    ("person.face", person_face + n * 2)):
                self.memory_session.set_write_guard(
                    key, address, self.memory_session.read_bytes(address, 2))
        #攻击力
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x11, ctypes.byref(data), 2, None)
        self.data_input_4.setText(str(data.value))
        #防御力
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x13, ctypes.byref(data), 2, None)
        self.data_input_5.setText(str(data.value))
        #精神力
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x15, ctypes.byref(data), 2, None)
        self.data_input_6.setText(str(data.value))
        #爆发力
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x17, ctypes.byref(data), 2, None)
        self.data_input_7.setText(str(data.value))
        #士气
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x19, ctypes.byref(data), 2, None)
        self.data_input_8.setText(str(data.value))
        #HP上限
        if version in (-1, EngineVersion.V64, EngineVersion.V65,
                       EngineVersion.V63_MP, EngineVersion.V66):
            hp_max = self.memory_session.read_u32(mem.value+0x48*n+0x1B)
        else:
            hp_max = self.memory_session.read_u32(mem.value+0x48*n+0x1C)
        self.data_input_9.setText(str(hp_max))
        data = ctypes.c_int(0)
        #MP上限
        if version in (-1, EngineVersion.V64, EngineVersion.V65,
                       EngineVersion.V63_MP, EngineVersion.V66):
            mp_max = self.memory_session.read_u16(mem.value+0x48*n+0x1F)
        else:
            mp_max = self.memory_session.read_u8(mem.value+0x48*n+0x20)
        self.data_input_10.setText(str(mp_max))
        data = ctypes.c_int(0)
        #我军
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x2A, ctypes.byref(data), 1, None)
        if data.value==0:
            self.data_input_11.setChecked(True)
        else:
            self.data_input_11.setChecked(False)
        #等级
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x2C, ctypes.byref(data), 1, None)
        self.data_input_12.setText(str(data.value))
        #经验
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x2D, ctypes.byref(data), 1, None)
        self.data_input_13.setText(str(data.value))
        #武力
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x21, ctypes.byref(data), 1, None)
        self.data_input_14.setText(str(data.value))
        #统率
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x22, ctypes.byref(data), 1, None)
        self.data_input_15.setText(str(data.value))
        #智力
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x23, ctypes.byref(data), 1, None)
        self.data_input_16.setText(str(data.value))
        #敏捷
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x24, ctypes.byref(data), 1, None)
        self.data_input_17.setText(str(data.value))
        #运气
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x25, ctypes.byref(data), 1, None)
        self.data_input_18.setText(str(data.value))
        #出场
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x27, ctypes.byref(data), 1, None)
        self.data_input_19.setText(str(data.value))
        #撤退
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x29, ctypes.byref(data), 1, None)
        self.data_input_20.setText(str(data.value))
        gongxun = self._profile_address(
            "merit", 0x00508000 if version in (0, 5, 6) else 0x00508400)
        if version == 6 and n < self.engine_profile.limits["merit_count"]:
            self.memory_session.set_write_guard(
                "person.merit", gongxun + 10 * n,
                self.memory_session.read_bytes(gongxun + 10 * n, 10))
        if n <= 101:
            #五维功勋仅有 102 条记录。
            merit_values = [self.memory_session.read_u16(gongxun+10*n+offset)
                            for offset in (0, 2, 4, 6, 8)]
            for widget, value in zip((
                    self.data_input_21, self.data_input_22, self.data_input_23,
                    self.data_input_24, self.data_input_25), merit_values):
                widget.setText(str(value))
        #3
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x28, ctypes.byref(data), 1, None)
        self.data_input_26.setText(str(data.value))
        #6
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x6, ctypes.byref(data), 2, None)
        self.data_input_27.setText(str(data.value))
        #6
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x44, ctypes.byref(data), 2, None)
        self.data_input_28.setText(str(data.value))
        #6
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x46, ctypes.byref(data), 2, None)
        self.data_input_29.setText(str(data.value))
        #杀敌数
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x35, ctypes.byref(data), 2, None)
        self.data_input_30.setText(str(data.value))
        data = ctypes.c_int(0)
        #兵种
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x2B, ctypes.byref(data), 1, None)
        self.data_input_31.setCurrentIndex(data.value)
        #武器
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x2E, ctypes.byref(data), 1, None)
        self.data_input_32.setCurrentIndex(data.value)
        if data.value==255:
            self.data_input_32.setCurrentIndex(cnt_item)
        #等级
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x2F, ctypes.byref(data), 1, None)
        self.data_input_33.setText(str(data.value))
        #经验
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x30, ctypes.byref(data), 1, None)
        self.data_input_34.setText(str(data.value))
        #防具
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x31, ctypes.byref(data), 1, None)
        self.data_input_35.setCurrentIndex(data.value)
        if data.value==255:
            self.data_input_35.setCurrentIndex(cnt_item)
        #等级
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x32, ctypes.byref(data), 1, None)
        self.data_input_36.setText(str(data.value))
        #经验
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x33, ctypes.byref(data), 1, None)
        self.data_input_37.setText(str(data.value))
        #辅助
        self.md.ReadProcessMemory(int(self.p), mem.value+0x48*n+0x34, ctypes.byref(data), 1, None)
        self.data_input_38.setCurrentIndex(data.value)
        if data.value==255:
            self.data_input_38.setCurrentIndex(cnt_item)

        #无功勋人
        if n > 101:
            self.data_input_21.setText("")
            self.data_input_22.setText("")
            self.data_input_23.setText("")
            self.data_input_24.setText("")
            self.data_input_25.setText("")
            self.data_input_21.setEnabled(False)
            self.data_input_22.setEnabled(False)
            self.data_input_23.setEnabled(False)
            self.data_input_24.setEnabled(False)
            self.data_input_25.setEnabled(False)
        else:
            self.data_input_21.setEnabled(True)
            self.data_input_22.setEnabled(True)
            self.data_input_23.setEnabled(True)
            self.data_input_24.setEnabled(True)
            self.data_input_25.setEnabled(True)

    #扳手第二页保存按键
    def saveData(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        global cnt_item
        if version == 6 and not self._feature_enabled("person"):
            self._set_status(self.engine_profile.capability("person").reason)
            return
        if len(self.listview_data.selectedIndexes())<=0:
            return
        n = self.listview_data.selectedIndexes()[0].row()
        if not 0 <= n < self.engine_profile.limits["person_count"]:
            self._set_status("人物编号越界")
            return
        fields = [
            (self.data_input_1, 16, "R形象"),
            (self.data_input_2, 16, "S形象"),
            (self.data_input_3, 16, "头像"),
            (self.data_input_4, 16, "攻击力"),
            (self.data_input_5, 16, "防御力"),
            (self.data_input_6, 16, "精神力"),
            (self.data_input_7, 16, "爆发力"),
            (self.data_input_8, 16, "士气"),
            (self.data_input_9, 32, "HP上限"),
            (self.data_input_10, 16 if version in
             (-1, EngineVersion.V64, EngineVersion.V65,
              EngineVersion.V63_MP, EngineVersion.V66) else 8, "MP上限"),
            (self.data_input_12, 8, "等级"),
            (self.data_input_13, 8, "经验"),
            (self.data_input_14, 8, "武力"),
            (self.data_input_15, 8, "统率"),
            (self.data_input_16, 8, "智力"),
            (self.data_input_17, 8, "敏捷"),
            (self.data_input_18, 8, "运气"),
            (self.data_input_19, 8, "出场"),
            (self.data_input_20, 8, "撤退"),
            (self.data_input_26, 8, "人物字段28"),
            (self.data_input_27, 16, "人物字段06"),
            (self.data_input_28, 16, "人物字段44"),
            (self.data_input_29, 16, "人物字段46"),
            (self.data_input_30, 16, "杀敌数"),
            (self.data_input_33, 8, "武器等级"),
            (self.data_input_34, 8, "武器经验"),
            (self.data_input_36, 8, "防具等级"),
            (self.data_input_37, 8, "防具经验"),
        ]
        if n <= 101:
            fields.extend((widget, 16, label) for widget, label in (
                (self.data_input_21, "武力功勋"),
                (self.data_input_22, "统率功勋"),
                (self.data_input_23, "智力功勋"),
                (self.data_input_24, "敏捷功勋"),
                (self.data_input_25, "运气功勋"),
            ))
        try:
            for widget, bits, label in fields:
                self._parse_uint(widget, bits, label)
        except (ValueError, TypeError) as exc:
            self._set_status(str(exc))
            return
        if version in (EngineVersion.V64, EngineVersion.V65, EngineVersion.V66):
            # Person equipment bytes are category-relative engine tokens.  The
            # complete 0..254 catalog is visible, but consumable records must
            # never be written into these three slots.
            if not self._validate_equipment_selection(
                    self.data_input_32, 0, 69, "Weapon"):
                return
            if not self._validate_equipment_selection(
                    self.data_input_35, 70, 108, "Armor"):
                return
            if not self._validate_equipment_selection(
                    self.data_input_38, 109,
                    int(self.engine_profile.limits.get("equipment_count", 150)) - 1,
                    "Accessory"):
                return
        person_r = self._profile_address("person_r", 0x0050F800)
        person_s = self._profile_address("person_s", 0x00501000)
        person_face = self._profile_address("person_face", 0x0050F000)
        person_pointer = self._profile_address("person_pointer", 0x004CEA00)
        if version == 6:
            try:
                current_person_base = self.memory_session.read_u32(person_pointer)
                if current_person_base != getattr(
                        self, "_person_pointer_snapshot", None):
                    raise MemoryAccessError("人物数组指针已变化，请刷新后重试")
                self.memory_session.verify_write_guards("person.")
            except MemoryAccessError as exc:
                self._set_status(str(exc))
                return
        #R形象
        data = ctypes.c_int(int(self.data_input_1.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), person_r+n*2, ctypes.byref(data), 2, None)
        
        #S形象
        data = ctypes.c_int(int(self.data_input_2.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), person_s+n*2, ctypes.byref(data), 2, None)
        #头像
        data = ctypes.c_int(int(self.data_input_3.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), person_face+n*2, ctypes.byref(data), 2, None)
        mem = ctypes.c_uint32()
        mem.value = self.memory_session.read_u32(person_pointer)
        #攻击力
        data = ctypes.c_int(int(self.data_input_4.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x11, ctypes.byref(data), 2, None)
        #防御力
        data = ctypes.c_int(int(self.data_input_5.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x13, ctypes.byref(data), 2, None)
        #精神力
        data = ctypes.c_int(int(self.data_input_6.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x15, ctypes.byref(data), 2, None)
        #爆发力
        data = ctypes.c_int(int(self.data_input_7.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x17, ctypes.byref(data), 2, None)
        #士气
        data = ctypes.c_int(int(self.data_input_8.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x19, ctypes.byref(data), 2, None)
        #HP上限
        data = ctypes.c_int(int(self.data_input_9.toPlainText()))
        if version in (-1, EngineVersion.V64, EngineVersion.V65,
                       EngineVersion.V63_MP, EngineVersion.V66):
            self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x1B, ctypes.byref(data), 4, None)
        else:
            self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x1C, ctypes.byref(data), 4, None)
        #MP上限
        data = ctypes.c_int(int(self.data_input_10.toPlainText()))
        if version in (-1, EngineVersion.V64, EngineVersion.V65,
                       EngineVersion.V63_MP, EngineVersion.V66):
            self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x1F, ctypes.byref(data), 2, None)
        else:
            self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x20, ctypes.byref(data), 1, None)
        #我军
        if self.data_input_11.isChecked() == True:
            data=ctypes.c_int(0)
        else:
            data=ctypes.c_int(255)
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x2A, ctypes.byref(data), 1, None)
        #等级
        data = ctypes.c_int(int(self.data_input_12.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x2C, ctypes.byref(data), 1, None)
        #经验
        data = ctypes.c_int(int(self.data_input_13.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x2D, ctypes.byref(data), 1, None)
        #武力
        data = ctypes.c_int(int(self.data_input_14.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x21, ctypes.byref(data), 1, None)
        #统率
        data = ctypes.c_int(int(self.data_input_15.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x22, ctypes.byref(data), 1, None)
        #智力
        data = ctypes.c_int(int(self.data_input_16.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x23, ctypes.byref(data), 1, None)
        #敏捷
        data = ctypes.c_int(int(self.data_input_17.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x24, ctypes.byref(data), 1, None)
        #运气
        data = ctypes.c_int(int(self.data_input_18.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x25, ctypes.byref(data), 1, None)
        #出场
        data = ctypes.c_int(int(self.data_input_19.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x27, ctypes.byref(data), 1, None)
        #撤退
        data = ctypes.c_int(int(self.data_input_20.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x29, ctypes.byref(data), 1, None)
        if n <= 101:
            gongxun = self._profile_address(
                "merit", 0x00508000 if version in (0, 5, 6) else 0x00508400)
            #武力功勋
            data = ctypes.c_int(int(self.data_input_21.toPlainText()))
            if n <= 101:
                self.md.WriteProcessMemory(int(self.p), gongxun+10*n+0, ctypes.byref(data), 2, None)
            #统率功勋
            data = ctypes.c_int(int(self.data_input_22.toPlainText()))
            if n <= 101:
                self.md.WriteProcessMemory(int(self.p), gongxun+10*n+2, ctypes.byref(data), 2, None)
            #智力功勋
            data = ctypes.c_int(int(self.data_input_23.toPlainText()))
            if n <= 101:
                self.md.WriteProcessMemory(int(self.p), gongxun+10*n+4, ctypes.byref(data), 2, None)
            #敏捷功勋
            data = ctypes.c_int(int(self.data_input_24.toPlainText()))
            if n <= 101:
                self.md.WriteProcessMemory(int(self.p), gongxun+10*n+6, ctypes.byref(data), 2, None)
            #运气功勋
            data = ctypes.c_int(int(self.data_input_25.toPlainText()))
            if n <= 101:
                self.md.WriteProcessMemory(int(self.p), gongxun+10*n+8, ctypes.byref(data), 2, None)
        #3
        data = ctypes.c_int(int(self.data_input_26.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x28, ctypes.byref(data), 1, None)
        #6
        data = ctypes.c_int(int(self.data_input_27.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x6, ctypes.byref(data), 2, None)
        #6
        data = ctypes.c_int(int(self.data_input_28.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x44, ctypes.byref(data), 2, None)
        #6
        data = ctypes.c_int(int(self.data_input_29.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x46, ctypes.byref(data), 2, None)
        #杀敌数
        data = ctypes.c_int(int(self.data_input_30.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x35, ctypes.byref(data), 2, None)
        #兵种
        data = ctypes.c_int(self.data_input_31.currentIndex())
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x2B, ctypes.byref(data), 1, None)
        #武器
        data = ctypes.c_int(self.data_input_32.currentIndex())
        if self.data_input_32.currentIndex() == cnt_item:
            data=ctypes.c_int(255)
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x2E, ctypes.byref(data), 1, None)
        #等级
        data = ctypes.c_int(int(self.data_input_33.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x2F, ctypes.byref(data), 1, None)
        #经验
        data = ctypes.c_int(int(self.data_input_34.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x30, ctypes.byref(data), 1, None)
        #防具
        data = ctypes.c_int(self.data_input_35.currentIndex())
        if self.data_input_35.currentIndex() == cnt_item:
            data=ctypes.c_int(255)
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x31, ctypes.byref(data), 1, None)
        #等级
        data = ctypes.c_int(int(self.data_input_36.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x32, ctypes.byref(data), 1, None)
        #经验
        data = ctypes.c_int(int(self.data_input_37.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x33, ctypes.byref(data), 1, None)
        #辅助
        data = ctypes.c_int(self.data_input_38.currentIndex())
        if self.data_input_38.currentIndex() == cnt_item:
            data=ctypes.c_int(255)
        self.md.WriteProcessMemory(int(self.p), mem.value+0x48*n+0x34, ctypes.byref(data), 1, None)

    #扳手第三页人物战场数据读入
    def onWar(self, kind):
        if self._detaching or self.memory_session is None or self.engine_profile is None:
            return
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        if version == 6 and not self._feature_readable("battle"):
            self._set_status(self.engine_profile.capability("battle").reason)
            return
        save_state = self._profile_address("save_state", 0x004B0770)
        battle_state = self._profile_address("battle_state", 0x004B3D08)
        battle_sp = self._profile_address("battle_sp", 0x00501C00)
        person_pointer = self._profile_address("person_pointer", 0x004CEA00)
        data = ctypes.c_int()
        data2 = ctypes.c_int()
        name = ctypes.c_byte()
        mem = ctypes.c_uint32()
        if kind == 0:
            n = 0
            if len(self.listview_war.selectedIndexes())>0:
                n = self.listview_war.selectedIndexes()[0].row()
            self.listview_war.clear()
            mem.value = self.memory_session.read_u32(person_pointer)
            for i in range(0,cnt_wo + cnt_you + cnt_di):
                self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war, ctypes.byref(data), 2, None)
                if data.value!=65535:
                    self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war + 5, ctypes.byref(data2), 1, None)
                    if (data2.value==0 and self.war_kind_1.isChecked() == True)\
                        or (data2.value==1 and self.war_kind_2.isChecked() == True)\
                        or (data2.value>=2 and self.war_kind_3.isChecked() == True):
                        names = self._read_text(
                            mem.value+data.value*0x48+8, 8)
                        self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war+4, ctypes.byref(data2), 1, None)
                        item = QtWidgets.QListWidgetItem(
                            str(data2.value)+"/"+str(data.value)+":"+names)
                        item.setData(QtCore.Qt.UserRole, i)
                        self.listview_war.addItem(item)
            if n >= self.listview_war.count():
                n = 0
            if self.listview_war.count()!=0:
                self.listview_war.setCurrentRow(n)

        war_code = self._selected_war_slot()
        if war_code < 0:
            return
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war, ctypes.byref(data), 2, None)
        data_code = data.value
        if version == 6:
            self.memory_session.clear_write_guards("battle.")
            for key, address, size in (
                    ("battle.unit", addr_war + war_code * len_war, len_war),
                    ("battle.sp", battle_sp,
                     self.engine_profile.limits["person_count"]),
                    ("battle.state", battle_state, 0x12),
                    ("battle.save", save_state, 7)):
                self.memory_session.set_write_guard(
                    key, address, self.memory_session.read_bytes(address, size))

        #角色
        self.war_input_1.setCurrentIndex(data_code)
        #HPCur
        hp_current = self.memory_session.read_u32(
            addr_war+war_code*len_war+0x10)
        self.war_input_2.setText(str(hp_current))
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xC, ctypes.byref(data2), 1, None)
        # The dead-unit state controls the button.  Capability evidence is a
        # warning in direct compatibility mode, never a second UI gate.
        if hp_current == 0 and data2.value == 3:
            self.war_life.setEnabled(True)
            self.war_life.setStyleSheet("background-color:rgb(255, 255, 255);\ncolor:rgb(0,0,0)")
        else:
            self.war_life.setEnabled(False)
            self.war_life.setStyleSheet("background-color:rgb(255, 255, 255);\ncolor:rgb(190,190,190)")
        #MPCur
        self.war_input_3.setText(str(self.memory_session.read_u32(
            addr_war+war_code*len_war+0x14)))
        #SPCur
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), battle_sp+data_code, ctypes.byref(data), 1, None)
        self.war_input_4.setText(str(data.value))
        #健康
        global condition_change
        data = ctypes.c_int()
        if condition_change == False:
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x1E, ctypes.byref(data), 1, None)
            self.war_input_5.setChecked(data.value & (1 << 1))
            self.war_input_6.setChecked(data.value & (1 << 2))
            self.war_input_7.setChecked(data.value & (1 << 3))
            self.war_input_8.setChecked(data.value & (1 << 4))
        else:
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x1E, ctypes.byref(data), 1, None)
            self.war_input_51.setText(str(data.value % 4))
            self.war_input_52.setText(str(int(data.value / 4) % 4))
            self.war_input_53.setText(str(int(data.value / 16) % 4))
            self.war_input_54.setText(str(int(data.value / 64) % 4))
        #X
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x6, ctypes.byref(data), 1, None)
        self.war_input_9.setText(str(data.value))
        #Y
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x7, ctypes.byref(data), 1, None)
        self.war_input_10.setText(str(data.value))
        self.war_input_9.setEnabled(self.war_life.isEnabled())
        self.war_input_10.setEnabled(self.war_life.isEnabled())
        #方针
        if self.war_kind_1.isChecked() == False:
            self.war_11.show()
            self.war_input_11.show()
            self.war_13.show()
            self.war_input_13.show()
            self.war_14.show()
            self.war_input_14.show()
            self.war_36.show()
            self.war_input_36.show()
            self.war_37.show()
            self.war_input_37.show()
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(data), 1, None)
            if data.value == 3:
                self.war_input_11.setCurrentIndex(0)
            if data.value == 1:
                self.war_input_11.setCurrentIndex(1)
            if data.value == 2:
                self.war_input_11.setCurrentIndex(2)
            if data.value == 0:
                self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x8, ctypes.byref(data2), 1, None)
                if data2.value != 255:
                    self.war_input_11.setCurrentIndex(3)
                else:
                    self.war_input_11.setCurrentIndex(4)
            if data.value == 4:
                self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x8, ctypes.byref(data2), 1, None)
                if data2.value != 255:
                    self.war_input_11.setCurrentIndex(5)
                else:
                    self.war_input_11.setCurrentIndex(6)
            if (data.value == 0 or data.value == 4) and data2.value != 255:
                self.war_12.show()
                self.war_input_12.show()
                self.war_input_12.clear()
                data2 = ctypes.c_int()
                data3 = ctypes.c_int()
                name = ctypes.c_byte()
                mem = ctypes.c_uint32()
                mem.value = self.memory_session.read_u32(person_pointer)
                self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x8, ctypes.byref(data3), 1, None)
                for i in range(0,cnt_wo + cnt_you + cnt_di):
                    self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war, ctypes.byref(data), 2, None)
                    if data.value!=65535:
                        self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war + 5, ctypes.byref(data2), 1, None)
                        names = self._read_text(
                            mem.value+data.value*0x48+8, 8)
                        self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war+4, ctypes.byref(data2), 1, None)
                        self.war_input_12.addItem(str(data2.value)+":"+names, i)
                    if i == data3.value:
                        self.war_input_12.setCurrentIndex(self.war_input_12.count()-1)
                        global_1 = data3.value
            else:
                self.war_12.hide()
                self.war_input_12.hide()
            data = ctypes.c_int()
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x9, ctypes.byref(data), 1, None)
            self.war_input_13.setText(str(data.value))
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xA, ctypes.byref(data), 1, None)
            self.war_input_14.setText(str(data.value))
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x20, ctypes.byref(data), 1, None)
            self.war_input_36.setText(str(data.value))
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x21, ctypes.byref(data), 1, None)
            self.war_input_37.setText(str(data.value))
        else:
            self.war_11.hide()
            self.war_input_11.hide()
            self.war_12.hide()
            self.war_input_12.hide()
            self.war_13.hide()
            self.war_input_13.hide()
            self.war_14.hide()
            self.war_input_14.hide()
            self.war_36.hide()
            self.war_input_36.hide()
            self.war_37.hide()
            self.war_input_37.hide()
        #行动
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xD, ctypes.byref(data), 1, None)
        if data.value & (1 << 1) or data.value & (1 << 2):
            self.war_input_15.setChecked(True)
        else:
            self.war_input_15.setChecked(False)
        #可控
        if self.war_kind_1.isChecked() == True or self.war_kind_3.isChecked() == True:
            self.war_46.hide()
            self.war_input_46.hide()
        if self.war_kind_2.isChecked() == True:
            self.war_46.show()
            self.war_input_46.show()
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(data), 1, None)
            if data.value == 7:
                self.war_input_46.setChecked(True)
                self.war_input_11.setCurrentIndex(0)
            else:
                self.war_input_46.setChecked(False)
        #方向
        '''data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xF, ctypes.byref(data), 1, None)
        self.war_input_16.setCurrentIndex(data.value)'''
        self.war_input_16.setCurrentIndex(0)
        #攻击
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x18, ctypes.byref(data), 1, None)
        if data.value < 3:
            self.war_input_17.setCurrentIndex(0)
        if data.value == 3:
            self.war_input_17.setCurrentIndex(1)
        if data.value > 3:
            self.war_input_17.setCurrentIndex(2)
        #攻击
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x19, ctypes.byref(data), 1, None)
        if data.value < 3:
            self.war_input_18.setCurrentIndex(0)
        if data.value == 3:
            self.war_input_18.setCurrentIndex(1)
        if data.value > 3:
            self.war_input_18.setCurrentIndex(2)
        #攻击
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x1A, ctypes.byref(data), 1, None)
        if data.value < 3:
            self.war_input_19.setCurrentIndex(0)
        if data.value == 3:
            self.war_input_19.setCurrentIndex(1)
        if data.value > 3:
            self.war_input_19.setCurrentIndex(2)
        #攻击
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x1B, ctypes.byref(data), 1, None)
        if data.value < 3:
            self.war_input_20.setCurrentIndex(0)
        if data.value == 3:
            self.war_input_20.setCurrentIndex(1)
        if data.value > 3:
            self.war_input_20.setCurrentIndex(2)
        #攻击
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x1C, ctypes.byref(data), 1, None)
        if data.value < 3:
            self.war_input_21.setCurrentIndex(0)
        if data.value == 3:
            self.war_input_21.setCurrentIndex(1)
        if data.value > 3:
            self.war_input_21.setCurrentIndex(2)
        #攻击
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x1D, ctypes.byref(data), 1, None)
        if data.value < 3:
            self.war_input_22.setCurrentIndex(0)
        if data.value == 3:
            self.war_input_22.setCurrentIndex(1)
        if data.value > 3:
            self.war_input_22.setCurrentIndex(2)
        #回合上限
        self.md.ReadProcessMemory(int(self.p), battle_state+3, ctypes.byref(data), 1, None)
        self.war_input_24.setText(str(data.value))
        #当前回合
        self.md.ReadProcessMemory(int(self.p), battle_state+2, ctypes.byref(data), 1, None)
        self.war_input_25.setText(str(data.value+1))
        #下一关
        self.md.ReadProcessMemory(int(self.p), save_state+6, ctypes.byref(data), 1, None)
        self.war_input_26.setText(str((int)(data.value/2)+1))
        #天气
        data = ctypes.c_int()
        data2 = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), battle_state+4, ctypes.byref(data), 1, None)
        self.md.ReadProcessMemory(int(self.p), battle_state+0x11, ctypes.byref(data2), 1, None)
        weather_list = [0,0,0,1,2,3,0,0,0,0,1,2,0,1,2,2,3,3,0,0,1,1,4,4,0,1,4,4,4,4]
        self.war_input_27.setCurrentIndex(weather_list[(data2.value*6+data.value)%30])
        #22
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x22, ctypes.byref(data), 1, None)
        self.war_input_38.setText(str(data.value))
        #戮
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x23, ctypes.byref(data), 1, None)
        self.war_input_39.setText(str(data.value))

        if version in (EngineVersion.V64, EngineVersion.V65,
                       EngineVersion.V66):
            self.war_input_40.setEnabled(True)
            self.war_input_41.setEnabled(True)
            self.war_input_42.setEnabled(True)
            self.war_input_43.setEnabled(True)
            self.war_input_44.setEnabled(True)
            self.war_input_45.setEnabled(True)
            #24
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x24, ctypes.byref(data), 1, None)
            self.war_input_40.setText(str(data.value))
            #26
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x26, ctypes.byref(data), 1, None)
            self.war_input_44.setText(str(data.value))
            #28
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x28, ctypes.byref(data), 1, None)
            self.war_input_41.setText(str(data.value))
            #2A
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x2A, ctypes.byref(data), 1, None)
            self.war_input_45.setText(str(data.value))
            #2C
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x2C, ctypes.byref(data), 1, None)
            self.war_input_42.setText(str(data.value))
            #2E
            self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0x2E, ctypes.byref(data), 1, None)
            self.war_input_43.setText(str(data.value))
        else:
            self.war_input_40.setEnabled(False)
            self.war_input_41.setEnabled(False)
            self.war_input_42.setEnabled(False)
            self.war_input_43.setEnabled(False)
            self.war_input_44.setEnabled(False)
            self.war_input_45.setEnabled(False)

    #扳手第三页人物战场数据保存
    def saveWar(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        if version == 6 and not self._feature_enabled("battle"):
            self._set_status(self.engine_profile.capability("battle").reason)
            return
        save_state = self._profile_address("save_state", 0x004B0770)
        battle_state = self._profile_address("battle_state", 0x004B3D08)
        battle_sp = self._profile_address("battle_sp", 0x00501C00)
        if self.listview_war.currentRow()<0:
            return
        war_code = self._selected_war_slot()
        if war_code < 0:
            return
        try:
            for widget, bits, label in (
                    (self.war_input_2, 32, "当前HP"),
                    (self.war_input_3, 32, "当前MP"),
                    (self.war_input_4, 8, "当前SP"),
                    (self.war_input_24, 8, "回合上限"),
                    (self.war_input_25, 8, "当前回合"),
                    (self.war_input_26, 8, "下一关"),
                    (self.war_input_38, 8, "战场字段22"),
                    (self.war_input_39, 8, "战场字段23")):
                self._parse_uint(widget, bits, label)
            if int(self.war_input_26.toPlainText()) > 128:
                raise ValueError("下一关超出可编码范围 0..128")
            if not self.war_kind_1.isChecked():
                for widget, label in (
                        (self.war_input_13, "方针X坐标"),
                        (self.war_input_14, "方针Y坐标"),
                        (self.war_input_36, "目标X坐标"),
                        (self.war_input_37, "目标Y坐标")):
                    self._parse_uint(widget, 8, label)
            if version in (EngineVersion.V64, EngineVersion.V65,
                           EngineVersion.V66):
                for widget, label in (
                        (self.war_input_40, "战场字段24"),
                        (self.war_input_41, "战场字段28"),
                        (self.war_input_42, "战场字段2C"),
                        (self.war_input_43, "战场字段2E"),
                        (self.war_input_44, "战场字段26"),
                        (self.war_input_45, "战场字段2A")):
                    self._parse_uint(widget, 8, label)
        except (ValueError, TypeError) as exc:
            self._set_status(str(exc))
            return
        if version == 6:
            try:
                self.memory_session.verify_write_guards("battle.")
            except MemoryAccessError as exc:
                self._set_status(str(exc))
                return
        data = ctypes.c_int()
        #检查RS
        self.md.ReadProcessMemory(int(self.p), save_state+6, ctypes.byref(data), 1, None)
        if data.value % 2 == 0:
            #HPCur
            data = ctypes.c_int(int(self.war_input_2.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x10, ctypes.byref(data), 4, None)
            #MPCur
            data = ctypes.c_int(int(self.war_input_3.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x14, ctypes.byref(data), 4, None)
            return
        
        data = ctypes.c_int()

        #角色
        data_code = self.war_input_1.currentIndex()
        data = ctypes.c_int(data_code)
        self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war, ctypes.byref(data), 2, None)
        #HPCur
        data = ctypes.c_int(int(self.war_input_2.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x10, ctypes.byref(data), 4, None)
        #MPCur
        data = ctypes.c_int(int(self.war_input_3.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x14, ctypes.byref(data), 4, None)
        #SPCur
        data = ctypes.c_int(int(self.war_input_4.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), battle_sp+data_code, ctypes.byref(data), 1, None)
        #健康
        global condition_change
        if condition_change == False:
            data = ctypes.c_int(0)
            if self.war_input_5.isChecked() == True:
                data.value += 2
            if self.war_input_6.isChecked() == True:
                data.value += 4
            if self.war_input_7.isChecked() == True:
                data.value += 8
            if self.war_input_8.isChecked() == True:
                data.value += 16
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1E, ctypes.byref(data), 1, None)
        else:
            data = ctypes.c_int(0)
            data.value += 1 * (int(self.war_input_51.text()) % 4)
            data.value += 4 * (int(self.war_input_52.text()) % 4)
            data.value += 16 * (int(self.war_input_53.text()) % 4)
            data.value += 64 * (int(self.war_input_54.text()) % 4)
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1E, ctypes.byref(data), 1, None)
        #方针
        if self.war_kind_1.isChecked() == False:
            sel = self.war_input_11.currentIndex()
            if sel == 0:
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(ctypes.c_int(3)), 1, None)
            if sel == 1:
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(ctypes.c_int(1)), 1, None)
            if sel == 2:
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(ctypes.c_int(2)), 1, None)
            if sel == 3 or sel == 4:
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(ctypes.c_int(0)), 1, None)
            if sel == 5 or sel == 6:
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(ctypes.c_int(4)), 1, None)
            if sel == 3 or sel == 5:
                target_slot = self.war_input_12.currentData()
                if target_slot is None:
                    target_slot = 0
                data = ctypes.c_int(int(target_slot))
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x8, ctypes.byref(data), 1, None)
            if sel == 4 or sel == 6:
                data = ctypes.c_int(int(self.war_input_13.toPlainText()))
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x9, ctypes.byref(data), 1, None)
                data = ctypes.c_int(int(self.war_input_14.toPlainText()))
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xA, ctypes.byref(data), 1, None)
                data = ctypes.c_int(255)
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x8, ctypes.byref(data), 1, None)
        #两个坐标
        if self.war_kind_1.isChecked() == False:
            data = ctypes.c_int(int(self.war_input_13.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x9, ctypes.byref(data), 1, None)
            data = ctypes.c_int(int(self.war_input_14.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xA, ctypes.byref(data), 1, None)
            data = ctypes.c_int(int(self.war_input_36.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x20, ctypes.byref(data), 1, None)
            data = ctypes.c_int(int(self.war_input_37.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x21, ctypes.byref(data), 1, None)
        #行动
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xD, ctypes.byref(data), 1, None)
        if self.war_input_15.isChecked() == True:
            data = ctypes.c_int(data.value | 6)
        else:
            data = ctypes.c_int(data.value & 248)
        self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xD, ctypes.byref(data), 1, None)
        #可控
        if self.war_kind_2.isChecked() == True:
            if self.war_input_46.isChecked() == True:
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0xE, ctypes.byref(ctypes.c_int(7)), 1, None)
        #转向
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war+0xF, ctypes.byref(data), 1, None)
        if self.war_input_16.currentIndex() != data.value + 1:
            self.onTurn()
        #攻击
        if self.war_input_17.currentIndex() == 0:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x18, ctypes.byref(ctypes.c_int(1)), 1, None)
        if self.war_input_17.currentIndex() == 1:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x18, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_17.currentIndex() == 2:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x18, ctypes.byref(ctypes.c_int(6)), 1, None)
        #攻击
        if self.war_input_18.currentIndex() == 0:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x19, ctypes.byref(ctypes.c_int(1)), 1, None)
        if self.war_input_18.currentIndex() == 1:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x19, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_18.currentIndex() == 2:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x19, ctypes.byref(ctypes.c_int(6)), 1, None)
        #攻击
        if self.war_input_19.currentIndex() == 0:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1A, ctypes.byref(ctypes.c_int(1)), 1, None)
        if self.war_input_19.currentIndex() == 1:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1A, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_19.currentIndex() == 2:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1A, ctypes.byref(ctypes.c_int(6)), 1, None)
        #攻击
        if self.war_input_20.currentIndex() == 0:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1B, ctypes.byref(ctypes.c_int(1)), 1, None)
        if self.war_input_20.currentIndex() == 1:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1B, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_20.currentIndex() == 2:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1B, ctypes.byref(ctypes.c_int(6)), 1, None)
        #攻击
        if self.war_input_21.currentIndex() == 0:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1C, ctypes.byref(ctypes.c_int(1)), 1, None)
        if self.war_input_21.currentIndex() == 1:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1C, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_21.currentIndex() == 2:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1C, ctypes.byref(ctypes.c_int(6)), 1, None)
        #攻击
        if self.war_input_22.currentIndex() == 0:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1D, ctypes.byref(ctypes.c_int(1)), 1, None)
        if self.war_input_22.currentIndex() == 1:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1D, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_22.currentIndex() == 2:
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x1D, ctypes.byref(ctypes.c_int(6)), 1, None)
        #全部
        if self.war_input_23.currentIndex() == 1:
            for i in range(0x18,0x1E):
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+i, ctypes.byref(ctypes.c_int(1)), 1, None)
        if self.war_input_23.currentIndex() == 2:
            for i in range(0x18,0x1E):
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+i, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_23.currentIndex() == 3:
            for i in range(0x18,0x1E):
                self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+i, ctypes.byref(ctypes.c_int(6)), 1, None)
        self.war_input_23.setCurrentIndex(0)
        #回合上限
        data = ctypes.c_int(int(self.war_input_24.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), battle_state+3, ctypes.byref(data), 1, None)
        #当前回合
        data = ctypes.c_int(int(self.war_input_25.toPlainText())-1)
        self.md.WriteProcessMemory(int(self.p), battle_state+2, ctypes.byref(data), 1, None)
        #下一关
        self.md.ReadProcessMemory(int(self.p), save_state+6, ctypes.byref(data), 1, None)
        if data.value % 2 == 1:
            data = ctypes.c_int(int(self.war_input_26.toPlainText()) * 2 - 1)
            if data.value >= 0:
                self.md.WriteProcessMemory(int(self.p), save_state+6, ctypes.byref(data), 1, None)
        #天气
        if self.war_input_27.currentIndex() == 0:
            self.md.WriteProcessMemory(int(self.p), battle_state+4, ctypes.byref(ctypes.c_int(0)), 1, None)
        if self.war_input_27.currentIndex() == 1:
            self.md.WriteProcessMemory(int(self.p), battle_state+4, ctypes.byref(ctypes.c_int(3)), 1, None)
        if self.war_input_27.currentIndex() == 2:
            self.md.WriteProcessMemory(int(self.p), battle_state+4, ctypes.byref(ctypes.c_int(4)), 1, None)
        if self.war_input_27.currentIndex() == 3:
            self.md.WriteProcessMemory(int(self.p), battle_state+4, ctypes.byref(ctypes.c_int(5)), 1, None)
        if self.war_input_27.currentIndex() == 4:
            self.md.WriteProcessMemory(int(self.p), battle_state+4, ctypes.byref(ctypes.c_int(0x16)), 1, None)
        self.md.WriteProcessMemory(int(self.p), battle_state+0x11, ctypes.byref(ctypes.c_int(0)), 1, None)
        #22
        data = ctypes.c_int(int(self.war_input_38.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x22, ctypes.byref(data), 1, None)
        #戮
        data = ctypes.c_int(int(self.war_input_39.toPlainText()))
        self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x23, ctypes.byref(data), 1, None)

        #群体buff/debuff
        if self.war_input_28.currentIndex() == 0:
            a = 0
            b = cnt_wo
        if self.war_input_28.currentIndex() == 1:
            a = cnt_wo
            b = cnt_wo + cnt_you
        if self.war_input_28.currentIndex() == 2:
            a = cnt_wo + cnt_you
            b = cnt_wo + cnt_you + cnt_di
        if self.war_input_29.currentIndex() != 0:
            for i in range(a,b):
                self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0x18, ctypes.byref(ctypes.c_int(2*self.war_input_29.currentIndex()-1)), 1, None)
        if self.war_input_30.currentIndex() != 0:
            for i in range(a,b):
                self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0x19, ctypes.byref(ctypes.c_int(2*self.war_input_30.currentIndex()-1)), 1, None)
        if self.war_input_31.currentIndex() != 0:
            for i in range(a,b):
                self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0x1A, ctypes.byref(ctypes.c_int(2*self.war_input_31.currentIndex()-1)), 1, None)
        if self.war_input_32.currentIndex() != 0:
            for i in range(a,b):
                self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0x1B, ctypes.byref(ctypes.c_int(2*self.war_input_32.currentIndex()-1)), 1, None)
        if self.war_input_33.currentIndex() != 0:
            for i in range(a,b):
                self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0x1C, ctypes.byref(ctypes.c_int(2*self.war_input_33.currentIndex()-1)), 1, None)
        if self.war_input_34.currentIndex() != 0:
            for i in range(a,b):
                self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+0x1D, ctypes.byref(ctypes.c_int(2*self.war_input_34.currentIndex()-1)), 1, None)
        if self.war_input_35.currentIndex() != 0:
            for i in range(a,b):
                for j in range(0x18,0x1E):
                    self.md.WriteProcessMemory(int(self.p), addr_war+i*len_war+j, ctypes.byref(ctypes.c_int(2*self.war_input_35.currentIndex()-1)), 1, None)
        self.war_input_29.setCurrentIndex(0)
        self.war_input_30.setCurrentIndex(0)
        self.war_input_31.setCurrentIndex(0)
        self.war_input_32.setCurrentIndex(0)
        self.war_input_33.setCurrentIndex(0)
        self.war_input_34.setCurrentIndex(0)
        self.war_input_35.setCurrentIndex(0)


        if version in (EngineVersion.V64, EngineVersion.V65,
                       EngineVersion.V66):
            #24
            data = ctypes.c_int(int(self.war_input_40.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x24, ctypes.byref(data), 1, None)
            #26
            data = ctypes.c_int(int(self.war_input_44.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x26, ctypes.byref(data), 1, None)
            #28
            data = ctypes.c_int(int(self.war_input_41.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x28, ctypes.byref(data), 1, None)
            #2A
            data = ctypes.c_int(int(self.war_input_45.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x2A, ctypes.byref(data), 1, None)
            #2C
            data = ctypes.c_int(int(self.war_input_42.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x2C, ctypes.byref(data), 1, None)
            #2E
            data = ctypes.c_int(int(self.war_input_43.toPlainText()))
            self.md.WriteProcessMemory(int(self.p), addr_war+war_code*len_war+0x2E, ctypes.byref(data), 1, None)

        self.onWar(1)

    def onItem(self):
        if self._detaching or self.memory_session is None or self.engine_profile is None:
            return
        global cnt_item
        if self.listview_item.count() < 200:
            return
        if (self.listview_item.currentRow() < 0 and
                self.listview_item_2.currentRow() < 0):
            return
        if not self._feature_readable("warehouse"):
            self._set_status(self.engine_profile.capability("warehouse").reason)
            return
        warehouse = self._profile_address("warehouse", 0x004B0783)
        money = self._profile_address("money", 0x004B077C)
        alignment = self._profile_address("alignment", 0x004B0782)
        merit_pool = self._profile_address("merit_pool", 0x00505F40)
        self.item_input_2.setText("0")
        self.item_input_3.setText("0")
        data = ctypes.c_int()
        for i in range(0,200):
            self.md.ReadProcessMemory(int(self.p), warehouse + i * 3, ctypes.byref(data), 1, None)
            if data.value != 255 and data.value < cnt_item:
                self.listview_item.item(i).setText(self.item_input_1.itemText(data.value))
            elif data.value != 255:
                self.listview_item.item(i).setText("无效编号:" + str(data.value))
            else:
                self.listview_item.item(i).setText("空")
            if i == self.listview_item.currentRow():
                self.item_input_1.setCurrentIndex(
                    data.value if data.value < cnt_item else cnt_item)
                self.md.ReadProcessMemory(int(self.p), warehouse + i * 3 + 1, ctypes.byref(data), 1, None)
                self.item_input_2.setText(str(data.value))
                self.md.ReadProcessMemory(int(self.p), warehouse + i * 3 + 2, ctypes.byref(data), 1, None)
                self.item_input_3.setText(str(data.value))

        a = self.listview_item_2.currentRow()
        consumable_value_available = True
        if self._consumable_mapping() is not None:
            consumables = self._consumable_ids()
            if 0 <= a < len(consumables):
                data.value = self._read_consumable(consumables[a])
            else:
                consumable_value_available = False
                if 0 <= a < len(consumables):
                    self._set_status(
                        self.engine_profile.capability("item_consumables").reason)
        elif version in (EngineVersion.V63_MP, EngineVersion.V63,
                         EngineVersion.V62, EngineVersion.V61):
            if a <= 16:
                self.md.ReadProcessMemory(int(self.p), 0x4B09DB + a, ctypes.byref(data), 1, None)
            else:
                self.md.ReadProcessMemory(int(self.p), 0x510c80 + a - 17, ctypes.byref(data), 1, None)
        else:
            consumable_value_available = False
        self.item_input_4.setText(
            str(data.value) if consumable_value_available else "")

        self.item_input_5.setText(str(self.memory_session.read_u32(money)))

        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), alignment, ctypes.byref(data), 1, None)
        self.item_input_6.setText(str(data.value))

        self.item_input_7.setText(str(self.memory_session.read_u32(merit_pool)))
        self._item_global_snapshot = {
            "money": self.memory_session.read_bytes(money, 4),
            "alignment": self.memory_session.read_bytes(alignment, 1),
            "merit": self.memory_session.read_bytes(merit_pool, 4),
        }
        selected_slot = self.listview_item.currentRow()
        if 0 <= selected_slot < 200:
            self._warehouse_snapshot = {
                "slot": selected_slot,
                "row": self.memory_session.read_bytes(
                    warehouse + selected_slot * 3, 3),
            }
        else:
            self._warehouse_snapshot = None
    
    def saveItem(self):
        global cnt_item
        warehouse = self._profile_address("warehouse", 0x004B0783)
        money = self._profile_address("money", 0x004B077C)
        alignment = self._profile_address("alignment", 0x004B0782)
        merit_pool = self._profile_address("merit_pool", 0x00505F40)
        a = self.listview_item.currentRow()
        migrated_item_profile = version in (
            EngineVersion.V64, EngineVersion.V65, EngineVersion.V66)
        try:
            level = (self._parse_uint(self.item_input_2, 8, "宝物等级")
                     if 0 <= a < 200 else 0)
            experience = (self._parse_uint(self.item_input_3, 8, "宝物经验")
                          if 0 <= a < 200 else 0)
            money_value = self._parse_uint(self.item_input_5, 32, "金钱")
            alignment_value = self._parse_uint(self.item_input_6, 8, "忠奸度")
            merit_value = self._parse_uint(self.item_input_7, 32, "功勋池")
            consumable_value = None
            mapping = self._consumable_mapping()
            if mapping is not None and self.listview_item_2.currentRow() >= 0:
                consumable_value = self._parse_uint(
                    self.item_input_4, int(mapping["width"]) * 8, "消耗品数量")
        except (ValueError, TypeError) as exc:
            self._set_status(str(exc))
            return
        if migrated_item_profile:
            entries = []
            if 0 <= a < 200:
                row = bytearray(self.memory_session.read_bytes(
                    warehouse + a * 3, 3))
                raw_item = int(row[0])
                selected_item = self.item_input_1.currentIndex()
                if selected_item != cnt_item:
                    if not self._is_item_record(selected_item):
                        self._set_status("Invalid warehouse item selection")
                        return
                    if (selected_item != raw_item and
                            not self._is_equipment_item(selected_item)):
                        self._set_status("Consumables cannot be written to equipment warehouse")
                        return
                    if selected_item != raw_item:
                        row[0] = selected_item
                        row[1] = level
                        row[2] = experience
                elif raw_item != 255 and raw_item < cnt_item:
                    row[0] = 255
                entries.append((warehouse + a * 3, bytes(row)))
            consumable_row = self.listview_item_2.currentRow()
            consumables = self._consumable_ids()
            if (consumable_value is not None and
                    0 <= consumable_row < len(consumables)):
                address, encoded = self._consumable_entry(
                    consumables[consumable_row], consumable_value)
                entries.append((address, encoded))
            entries.extend((
                (money, money_value.to_bytes(4, "little")),
                (alignment, bytes((alignment_value,))),
                (merit_pool, merit_value.to_bytes(4, "little")),
            ))
            try:
                for address, replacement in entries:
                    self.memory_session.write_bytes(
                        address, replacement, verify=False)
            except MemoryAccessError as exc:
                self._set_status(str(exc))
                return
            self.onItem.__call__()
            return
        if not 0 <= a < 200:
            self._set_status("仓库槽位必须在 0..199 范围内")
            return
        if self.item_input_1.currentIndex() != cnt_item:
            data = ctypes.c_int(self.item_input_1.currentIndex())
            self.md.WriteProcessMemory(int(self.p), warehouse + a * 3, ctypes.byref(data), 1, None)
            data = ctypes.c_int(level)
            self.md.WriteProcessMemory(int(self.p), warehouse + a * 3 + 1, ctypes.byref(data), 1, None)
            data = ctypes.c_int(experience)
            self.md.WriteProcessMemory(int(self.p), warehouse + a * 3 + 2, ctypes.byref(data), 1, None)
        else:
            data = ctypes.c_int(255)
            self.md.WriteProcessMemory(int(self.p), warehouse + a * 3, ctypes.byref(data), 1, None)

        a = self.listview_item_2.currentRow()
        data = ctypes.c_int(int(self.item_input_4.toPlainText() or "0"))
        if version == 6:
            consumables = self._consumable_ids()
            if (consumable_value is not None and 0 <= a < len(consumables)):
                address, encoded = self._consumable_entry(
                    consumables[a], consumable_value)
                expected = getattr(self, "_consumable_snapshots", {}).get(address)
                if expected is None:
                    raise MemoryAccessError("消耗品数量尚未读取，请先刷新")
                self.memory_session.compare_exchange_transaction((
                    CompareWriteEntry(address, expected, encoded),
                ))
                self._consumable_snapshots[address] = encoded
        elif version >= 1:
            if a <= 16:
                self.md.WriteProcessMemory(int(self.p), 0x4B09DB + a, ctypes.byref(data), 1, None)
            else:
                self.md.WriteProcessMemory(int(self.p), 0x510c80 + a-17, ctypes.byref(data), 1, None)
        else:
            self.md.WriteProcessMemory(int(self.p), 0x510c80 + a, ctypes.byref(data), 1, None)

        data = ctypes.c_int(money_value)
        self.md.WriteProcessMemory(int(self.p), money, ctypes.byref(data), 4, None)

        data = ctypes.c_int(alignment_value)
        self.md.WriteProcessMemory(int(self.p), alignment, ctypes.byref(data), 1, None)

        data = ctypes.c_int(merit_value)
        self.md.WriteProcessMemory(int(self.p), merit_pool, ctypes.byref(data), 4, None)

        self.onItem()

    def _effect_layout(self):
        if self.engine_profile is None:
            raise MemoryAccessError("尚未建立引擎画像")
        layout = self.engine_profile.resolved_feature("effect").layout
        if not isinstance(layout, TableLayoutSpec):
            raise MemoryAccessError("当前引擎没有已识别的特效表布局")
        return layout

    def _resolve_effect_table(self, layout=None):
        layout = layout or self._effect_layout()
        if layout.resolver == AddressResolverKind.FIXED_RVA:
            base = self.memory_session.resolve_reference(layout.reference)
            pointer_reference = None
        elif layout.resolver == AddressResolverKind.POINTER_RVA:
            try:
                base = self.memory_session.read_u32(layout.reference)
                pointer_reference = layout.reference
            except MemoryAccessError:
                # Compatibility for isolated UI fixtures and an already
                # resolved legacy caller.  Real attached sessions always keep
                # the pointer reference readable and therefore do not use it.
                global addr_tianfu
                if addr_tianfu <= 0:
                    raise
                base = int(addr_tianfu)
                pointer_reference = None
        else:
            raise MemoryAccessError("当前特效表地址解析方式尚未接线")
        if not self.memory_session.validate_actual_range(
                base, layout.stride, write=False):
            raise MemoryAccessError(
                "特效表首行不可读: 0x%08X+0x%X" % (base, layout.stride))
        if self.engine_profile is not None:
            descriptor = self.engine_profile.metadata.get(
                "table_descriptors", {}).get("effect")
            full_range_available = (
                isinstance(descriptor, ResolvedTableDescriptor) and
                self.memory_session.validate_actual_range(
                    base, descriptor.byte_size, write=False))
            if getattr(self.memory_session, "pid", None) is not None or full_range_available:
                self.engine_profile.refresh_table_descriptor_runtime(
                    "effect", self.memory_session, base)
        return base, pointer_reference

    def _effect_row_address_66(self, effect_id):
        layout = self._effect_layout()
        if not 0 <= effect_id < layout.record_count:
            raise ValueError("特效编号越界")
        base, _pointer = self._resolve_effect_table(layout)
        return base + effect_id * layout.stride

    def _read_effect_snapshot(self, effect_id):
        layout = self._effect_layout()
        if not 0 <= effect_id < layout.record_count:
            raise ValueError("特效编号越界")
        table_base, pointer_reference = self._resolve_effect_table(layout)
        address = table_base + effect_id * layout.stride
        raw = self.memory_session.read_actual_bytes(address, layout.stride)
        if layout.codec == "effect-4+2-v1":
            decoded = parse_effect_assignment_row_66(raw)
        elif layout.codec == "effect-3+1-v1":
            decoded = parse_effect_assignment_row_8(raw)
        else:
            raise MemoryAccessError("不支持的特效表编解码器: %s" % layout.codec)
        descriptor = self.engine_profile.metadata.get(
            "table_descriptors", {}).get("effect")
        generation = (descriptor.generation
                      if (isinstance(descriptor, ResolvedTableDescriptor) and
                          descriptor.generation)
                      else "%08X:%d:%d" %
                      (table_base, layout.stride, layout.record_count))
        snapshot = RawRecordSnapshot(
            "effect", address, raw, decoded, table_base,
            pointer_reference=pointer_reference,
            pointer_value=(table_base if pointer_reference is not None else None),
            generation=generation)
        self._effect_snapshot = snapshot
        return snapshot

    def _commit_effect_snapshot(self, replacement):
        snapshot = getattr(self, "_effect_snapshot", None)
        if not isinstance(snapshot, RawRecordSnapshot):
            raise MemoryAccessError("特效记录尚未读取，请先刷新")
        layout = self._effect_layout()
        if len(replacement) != len(snapshot.raw):
            raise MemoryAccessError("特效记录编码长度与读取快照不一致")
        self.memory_session.write_actual_bytes(
            snapshot.address, replacement, verify=False)
        decoded = (parse_effect_assignment_row_66(replacement)
                   if layout.codec == "effect-4+2-v1" else
                   parse_effect_assignment_row_8(replacement))
        self._effect_snapshot = RawRecordSnapshot(
            "effect", snapshot.address, replacement, decoded,
            snapshot.table_base, snapshot.pointer_reference,
            snapshot.pointer_value, snapshot.generation)

    def _read_exclusive_snapshot(self, effect_id):
        global addr_zhuanshu
        effect_count = self.engine_profile.limits.get("effect_count", 255)
        if not 0 <= effect_id < effect_count:
            raise ValueError("专属/套装特效编号越界")
        address = addr_zhuanshu + effect_id * 16
        raw = self.memory_session.read_bytes(address, 16)
        snapshot = RawRecordSnapshot(
            "effect_exclusive", address, raw, raw, addr_zhuanshu,
            generation="%08X:16:%d" % (addr_zhuanshu, effect_count))
        self._exclusive_snapshot = snapshot
        return snapshot

    def _exclusive_ui_state(self):
        members = []
        for index, (person, item, value) in enumerate((
                (self.power_input_2_1, self.power_input_2_2,
                 self.power_input_2_3),
                (self.power_input_2_4, self.power_input_2_5,
                 self.power_input_2_6)), 1):
            members.append((
                person.currentIndex(), item.currentIndex(),
                self._parse_uint(value, 8, "专属%d特效值" % index)))
        sets = []
        for index, (item1, item2, item3, value) in enumerate((
                (self.power_input_3_1, self.power_input_3_2,
                 self.power_input_3_3, self.power_input_3_4),
                (self.power_input_3_5, self.power_input_3_6,
                 self.power_input_3_7, self.power_input_3_8)), 1):
            sets.append((
                item1.currentIndex(), item2.currentIndex(),
                item3.currentIndex(),
                self._parse_uint(value, 8, "套装%d特效值" % index)))
        return tuple(members), tuple(sets)

    def _exclusive_state_from_raw(self, raw):
        global cnt_item
        if len(raw) != 16:
            raise ValueError("专属/套装记录必须正好是16字节")
        person_count = self.engine_profile.limits["person_count"]
        members = []
        for offset in (0, 4):
            raw_person = int.from_bytes(raw[offset:offset + 2], "little")
            raw_item = raw[offset + 2]
            raw_value = raw[offset + 3]
            members.append((
                raw_person if raw_person < person_count and raw_value != 0
                else person_count,
                raw_item if raw_item < cnt_item and raw_value != 0
                else cnt_item,
                raw_value,
            ))
        sets = []
        for offset in (8, 12):
            raw_value = raw[offset + 3]
            sets.append(tuple(
                raw_item if raw_item < cnt_item and raw_value != 0
                else cnt_item
                for raw_item in raw[offset:offset + 3]
            ) + (raw_value,))
        return tuple(members), tuple(sets)

    def _show_exclusive_assignment(self, raw):
        state = self._exclusive_state_from_raw(raw)
        for values, (person, item, value) in zip(state[0], (
                (self.power_input_2_1, self.power_input_2_2,
                 self.power_input_2_3),
                (self.power_input_2_4, self.power_input_2_5,
                 self.power_input_2_6))):
            person.setCurrentIndex(values[0])
            item.setCurrentIndex(values[1])
            value.setText(str(values[2]))
        for values, (item1, item2, item3, value) in zip(state[1], (
                (self.power_input_3_1, self.power_input_3_2,
                 self.power_input_3_3, self.power_input_3_4),
                (self.power_input_3_5, self.power_input_3_6,
                 self.power_input_3_7, self.power_input_3_8))):
            item1.setCurrentIndex(values[0])
            item2.setCurrentIndex(values[1])
            item3.setCurrentIndex(values[2])
            value.setText(str(values[3]))
        self._shown_exclusive_state = self._exclusive_ui_state()

    @staticmethod
    def _encode_exclusive_choice(value, empty_index, empty_value, label):
        if value == empty_index:
            return empty_value
        if 0 <= value < empty_index:
            return value
        raise ValueError("%s未选择有效目标" % label)

    def _encode_exclusive_equipment_choice(self, value, label):
        if value == cnt_item:
            return 255
        if self._is_equipment_item(value):
            return int(value)
        raise ValueError("%s cannot use an item/consumable ID" % label)

    def _collect_exclusive_assignment(self, effect_id=None):
        global cnt_item
        snapshot = getattr(self, "_exclusive_snapshot", None)
        shown = getattr(self, "_shown_exclusive_state", None)
        if not isinstance(snapshot, RawRecordSnapshot) or shown is None:
            if effect_id is None:
                raise MemoryAccessError("专属/套装记录缓存缺失且未提供特效编号")
            snapshot = self._read_exclusive_snapshot(effect_id)
            shown = self._exclusive_state_from_raw(snapshot.raw)
        current = self._exclusive_ui_state()
        replacement = bytearray(snapshot.raw)
        person_count = self.engine_profile.limits["person_count"]
        for index, (before, after) in enumerate(zip(shown[0], current[0])):
            if before == after:
                continue
            offset = index * 4
            if after[2] == 0:
                replacement[offset:offset + 4] = b"\0" * 4
                continue
            if before[0] != after[0]:
                person_id = self._encode_exclusive_choice(
                    after[0], person_count, person_count,
                    "专属%d角色" % (index + 1))
                replacement[offset:offset + 2] = person_id.to_bytes(
                    2, "little")
            if before[1] != after[1]:
                replacement[offset + 2] = self._encode_exclusive_equipment_choice(
                    after[1], "exclusive item")
            if before[2] != after[2]:
                replacement[offset + 3] = after[2]
        for index, (before, after) in enumerate(zip(shown[1], current[1])):
            if before == after:
                continue
            offset = 8 + index * 4
            if after[3] == 0:
                replacement[offset:offset + 4] = b"\0" * 4
                continue
            for item_index in range(3):
                if before[item_index] == after[item_index]:
                    continue
                replacement[offset + item_index] = (
                    self._encode_exclusive_equipment_choice(
                        after[item_index], "set item"))
            if before[3] != after[3]:
                replacement[offset + 3] = after[3]
        return bytes(replacement)

    def _write_exclusive_assignment(self, effect_id, replacement):
        global addr_zhuanshu
        address = addr_zhuanshu + effect_id * 16
        if len(replacement) != 16:
            raise ValueError("专属/套装记录编码长度错误")
        self.memory_session.write_bytes(address, replacement, verify=False)
        self._exclusive_snapshot = RawRecordSnapshot(
            "effect_exclusive", address, replacement, replacement,
            addr_zhuanshu, generation="%08X:16:%d" % (
                addr_zhuanshu,
                self.engine_profile.limits.get("effect_count", 255)))

    def _commit_power_snapshots(self, effect_id, effect_replacement,
                                exclusive_replacement):
        global addr_zhuanshu
        layout = self._effect_layout()
        if len(effect_replacement) != layout.stride:
            raise ValueError("特效记录编码长度错误")
        if len(exclusive_replacement) != 16:
            raise ValueError("专属/套装记录编码长度错误")
        table_base, pointer_reference = self._resolve_effect_table(layout)
        effect_address = table_base + effect_id * layout.stride
        exclusive_address = addr_zhuanshu + effect_id * 16
        self.memory_session.write_actual_bytes(
            effect_address, effect_replacement, verify=False)
        self.memory_session.write_bytes(
            exclusive_address, exclusive_replacement, verify=False)
        effect_decoded = (
            parse_effect_assignment_row_66(effect_replacement)
            if layout.codec == "effect-4+2-v1" else
            parse_effect_assignment_row_8(effect_replacement))
        self._effect_snapshot = RawRecordSnapshot(
            "effect", effect_address, effect_replacement,
            effect_decoded, table_base, pointer_reference,
            table_base if pointer_reference is not None else None,
            "%08X:%d:%d" %
            (table_base, layout.stride, layout.record_count))
        self._exclusive_snapshot = RawRecordSnapshot(
            "effect_exclusive", exclusive_address,
            exclusive_replacement, exclusive_replacement, addr_zhuanshu,
            generation="%08X:16:%d" %
            (addr_zhuanshu, self.engine_profile.limits.get(
                "effect_count", 255)))

    def _read_effect_assignment_66(self, effect_id):
        snapshot = self._read_effect_snapshot(effect_id)
        if not isinstance(snapshot.decoded, EffectAssignmentRow66):
            raise ValueError("当前特效记录不是 4角色2兵种布局")
        return snapshot.decoded

    def _show_effect_assignment_66(self, row):
        self._shown_effect_assignment_66 = row
        person_inputs = (self.power_input_1_1, self.power_input_1_2,
                         self.power_input_1_3, self.power_input_1_4)
        person_values = (self.power_input_1_5, self.power_input_1_6,
                         self.power_input_1_7, self.power_input_1_8)
        job_inputs = (self.power_input_1_9, self.power_input_1_11)
        job_values = (self.power_input_1_10, self.power_input_1_12)
        character_count = self.engine_profile.limits["person_count"]
        for slot, target, value in zip(row.characters, person_inputs,
                                       person_values):
            target.setCurrentIndex(slot.target_id
                                   if slot.target_id < character_count
                                   else character_count)
            value.setText(str(slot.effect_value))
        for slot, target, value in zip(row.jobs, job_inputs, job_values):
            target.setCurrentIndex(slot.target_id
                                   if slot.target_id < EFFECT_JOB_COUNT_66
                                   else EFFECT_JOB_COUNT_66)
            value.setText(str(slot.effect_value))

    def _collect_effect_assignment_66(self):
        person_inputs = (self.power_input_1_1, self.power_input_1_2,
                         self.power_input_1_3, self.power_input_1_4)
        person_values = (self.power_input_1_5, self.power_input_1_6,
                         self.power_input_1_7, self.power_input_1_8)
        job_inputs = (self.power_input_1_9, self.power_input_1_11)
        job_values = (self.power_input_1_10, self.power_input_1_12)
        characters = []
        character_count = self.engine_profile.limits["person_count"]
        shown_row = getattr(self, "_shown_effect_assignment_66", None)
        for index, (target, value) in enumerate(zip(person_inputs,
                                                     person_values)):
            target_id = target.currentIndex()
            if target_id == character_count:
                shown_target = (shown_row.characters[index].target_id
                                if shown_row is not None else -1)
                target_id = (shown_target
                             if shown_target >= character_count
                             else character_count)
            elif not 0 <= target_id < character_count:
                raise ValueError("角色%d未选择有效目标" % (index + 1))
            characters.append(EffectAssignmentSlot66(
                target_id, self._parse_uint(
                    value, 8, "角色%d特效值" % (index + 1))))
        jobs = []
        for index, (target, value) in enumerate(zip(job_inputs, job_values), 1):
            target_id = target.currentIndex()
            if target_id == EFFECT_JOB_COUNT_66:
                shown_target = (shown_row.jobs[index - 1].target_id
                                if shown_row is not None else -1)
                target_id = (shown_target
                             if shown_target >= EFFECT_JOB_COUNT_66 else 255)
            elif not 0 <= target_id < EFFECT_JOB_COUNT_66:
                raise ValueError("兵种%d未选择有效目标" % index)
            jobs.append(EffectAssignmentSlot66(
                target_id, self._parse_uint(value, 8, "兵种%d特效值" % index)))
        return EffectAssignmentRow66(tuple(characters), tuple(jobs))

    def _write_effect_assignment_66(self, effect_id, row):
        address = self._effect_row_address_66(effect_id)
        encoded = encode_effect_assignment_row_66(row)
        self.memory_session.write_actual_bytes(address, encoded, verify=False)

    def _show_effect_assignment_8(self, row):
        self._shown_effect_assignment_8 = row
        character_count = self.engine_profile.limits["person_count"]
        for target, target_id in zip(
                (self.power_input_1_1, self.power_input_1_2,
                 self.power_input_1_3), row.characters):
            target.setCurrentIndex(target_id if target_id < character_count
                                   else character_count)
        self.power_input_1_4.setCurrentIndex(
            row.job_id if row.job_id < EFFECT_JOB_COUNT_66
            else EFFECT_JOB_COUNT_66)
        self.power_input_1_5.setText(str(row.effect_value))

    def _collect_effect_assignment_8(self):
        character_count = self.engine_profile.limits["person_count"]
        shown = getattr(self, "_shown_effect_assignment_8", None)
        characters = []
        for index, target in enumerate((
                self.power_input_1_1, self.power_input_1_2,
                self.power_input_1_3)):
            target_id = target.currentIndex()
            if target_id == character_count:
                shown_target = (shown.characters[index]
                                if shown is not None else -1)
                target_id = (shown_target if shown_target >= character_count
                             else character_count)
            elif not 0 <= target_id < character_count:
                raise ValueError("角色%d未选择有效目标" % (index + 1))
            characters.append(target_id)
        job_id = self.power_input_1_4.currentIndex()
        if job_id == EFFECT_JOB_COUNT_66:
            shown_job = shown.job_id if shown is not None else -1
            job_id = shown_job if shown_job >= EFFECT_JOB_COUNT_66 else 255
        elif not 0 <= job_id < EFFECT_JOB_COUNT_66:
            raise ValueError("兵种未选择有效目标")
        return EffectAssignmentRow8(
            tuple(characters), job_id,
            self._parse_uint(self.power_input_1_5, 8, "天赋值"))

    def _write_effect_assignment_8(self, effect_id, row):
        address = self._effect_row_address_66(effect_id)
        self.memory_session.write_actual_bytes(
            address, encode_effect_assignment_row_8(row), verify=False)

    def onPower(self):
        if self._detaching or self.memory_session is None or self.engine_profile is None:
            return
        global cnt_item
        global addr_tianfu
        global addr_zhuanshu
        if self.listview_item_3.currentRow() < 0:
            return
        if not self._feature_readable("effect"):
            self._set_status(self.engine_profile.resolved_feature("effect").reason)
            return
        a = self.listview_item_3.currentRow()
        #天赋
        try:
            layout = self._effect_layout()
            snapshot = self._read_effect_snapshot(a)
            if layout.codec == "effect-4+2-v1":
                self._set_effect_layout(True)
                self._shown_effect_assignment_8 = None
                self._show_effect_assignment_66(snapshot.decoded)
            elif layout.codec == "effect-3+1-v1":
                self._set_effect_layout(False)
                self._shown_effect_assignment_66 = None
                self._show_effect_assignment_8(snapshot.decoded)
            else:
                raise ValueError("未知特效表布局: %s" % layout.codec)
        except (MemoryAccessError, ValueError) as exc:
            self._set_status("读取特效分配失败: %s" % exc)
            return
        if not self._feature_readable("effect_exclusive"):
            self._set_status(
                "特效分配已读取；专属/套装只读结构未识别，未继续读取")
            return
        try:
            exclusive = self._read_exclusive_snapshot(a)
            self._show_exclusive_assignment(exclusive.raw)
        except (MemoryAccessError, ValueError) as exc:
            self._set_status("读取专属/套装失败: %s" % exc)

    def savePower(self):
        if (not self._feature_writable("effect") or
                not self._feature_writable("effect_exclusive")):
            reasons = [self.engine_profile.resolved_feature(key).reason
                       for key in ("effect", "effect_exclusive")
                       if not self._feature_writable(key)]
            self._set_status("；".join(item for item in reasons if item) or
                             "天赋或专属/套装当前不可写")
            return
        a = self.listview_item_3.currentRow()
        if a < 0:
            return
        try:
            layout = self._effect_layout()
            if layout.codec == "effect-4+2-v1":
                effect_replacement = encode_effect_assignment_row_66(
                    self._collect_effect_assignment_66())
            elif layout.codec == "effect-3+1-v1":
                effect_replacement = encode_effect_assignment_row_8(
                    self._collect_effect_assignment_8())
            else:
                raise ValueError("未知特效表布局: %s" % layout.codec)
            exclusive_replacement = self._collect_exclusive_assignment(a)
            self._commit_power_snapshots(
                a, effect_replacement, exclusive_replacement)
        except (MemoryAccessError, ValueError) as exc:
            self._set_status("保存天赋/专属/套装失败: %s" % exc)
            return
        self.onPower()

    def onVar(self):
        if self._detaching or self.memory_session is None or self.engine_profile is None:
            return
        while self.listview_var_1.rowCount() != 0:
            self.listview_var_1.removeRow(0)
        while self.listview_var_2.rowCount() != 0:
            self.listview_var_2.removeRow(0)
        while self.listview_var_3.rowCount() != 0:
            self.listview_var_3.removeRow(0)
        data = ctypes.c_int()
        a = int(self.var_input_1.toPlainText())
        if a > 4096:
            a = 0
        values = self._variable_values("variables_bool", a)
        for offset, value in enumerate(values):
            i = a + offset
            data.value = value
            self.listview_var_1.insertRow(i-a)
            self.listview_var_1.setItem(i-a,0,QtWidgets.QTableWidgetItem(str(i)))
            if data.value == 0:
                self.listview_var_1.setItem(i-a,1,QtWidgets.QTableWidgetItem("False"))
                self.listview_var_1.item(i-a,1).setForeground(QtGui.QBrush(QtGui.QColor(0, 0, 255)))
            else:
                self.listview_var_1.setItem(i-a,1,QtWidgets.QTableWidgetItem("True"))
                self.listview_var_1.item(i-a,1).setForeground(QtGui.QBrush(QtGui.QColor(255, 0, 0)))
            self.listview_var_1.setItem(i-a,2,QtWidgets.QTableWidgetItem(
                '{:06X}'.format(self._variable_address("variables_bool", i))))
        a = int(self.var_input_2.toPlainText())
        if a > 4096:
            a = 0
        values = self._variable_values("variables_int", a)
        for offset, value in enumerate(values):
            i = a + offset
            data.value = value
            self.listview_var_2.insertRow(i-a)
            self.listview_var_2.setItem(i-a,0,QtWidgets.QTableWidgetItem(str(i)))
            self.listview_var_2.setItem(i-a,1,QtWidgets.QTableWidgetItem(str(data.value)))
            self.listview_var_2.setItem(i-a,2,QtWidgets.QTableWidgetItem(
                '{:06X}'.format(self._variable_address("variables_int", i))))
        a = int(self.var_input_3.toPlainText())
        if a > 4096:
            a = 0
        values = self._variable_values("variables_ptr", a)
        for offset, value in enumerate(values):
            i = a + offset
            data.value = value
            self.listview_var_3.insertRow(i-a)
            self.listview_var_3.setItem(i-a,0,QtWidgets.QTableWidgetItem(str(i)))
            self.listview_var_3.setItem(i-a,1,QtWidgets.QTableWidgetItem(str(data.value)))
            self.listview_var_3.setItem(i-a,2,QtWidgets.QTableWidgetItem(
                '{:06X}'.format(self._variable_address("variables_ptr", i))))

    def saveVar(self, kind):
        capability_key = {1: "variables_bool", 2: "variables_int",
                          3: "variables_ptr"}.get(kind)
        if version == 6 and not self._feature_enabled(capability_key):
            self._set_status(self.engine_profile.capability(capability_key).reason)
            return
        if kind == 1:
            if self.listview_var_1.currentRow() >= 0:
                row = self.listview_var_1.currentRow()
                addr = self.listview_var_1.item(row, 2)
                value = self.listview_var_1.item(row, 1)
                if value.data(0) == "True" or value.data(0) == "1" or value.data(0) == "t":
                    data = 1
                else:
                    data = 0
                layout = self._variable_layout("variables_bool")
                if layout.get("storage") == "bitset":
                    variable_item = self.listview_var_1.item(row, 0)
                    variable_index = int(variable_item.data(0))
                    address = self._variable_address(
                        "variables_bool", variable_index)
                    bit_mask = self._variable_bit_mask(layout, variable_index)
                    current = self.memory_session.read_u8(address)
                    changed = ((current | bit_mask) if data else
                               (current & ~bit_mask))
                    replacement = bytes((changed,))
                    self.memory_session.write_bytes(
                        address, replacement, verify=False)
                    self._variable_byte_snapshots[
                        ("variables_bool", address)] = replacement
                else:
                    width = int(layout["width"])
                    self.memory_session.write_bytes(
                        int(addr.data(0), 16),
                        data.to_bytes(width, "little"))
        elif kind == 2:
            if self.listview_var_2.currentRow() >= 0:
                addr = self.listview_var_2.item(self.listview_var_2.currentRow(),2)
                value = self.listview_var_2.item(self.listview_var_2.currentRow(),1)
                data = int(value.data(0))
                width = int(self._variable_layout("variables_int")["width"])
                address = int(addr.data(0), 16)
                replacement = data.to_bytes(width, "little", signed=data < 0)
                self.memory_session.write_bytes(
                    address, replacement, verify=False)
                self._variable_byte_snapshots[("variables_int", address)] = replacement
        elif kind == 3:
            if self.listview_var_3.currentRow() >= 0:
                addr = self.listview_var_3.item(self.listview_var_3.currentRow(),2)
                value = self.listview_var_3.item(self.listview_var_3.currentRow(),1)
                data = int(value.data(0))
                width = int(self._variable_layout("variables_ptr")["width"])
                address = int(addr.data(0), 16)
                replacement = data.to_bytes(width, "little", signed=data < 0)
                self.memory_session.write_bytes(
                    address, replacement, verify=False)
                self._variable_byte_snapshots[("variables_ptr", address)] = replacement
        self.onVar() 

    def saveDIY(self):
        if not self._feature_writable("diy"):
            reason = (self.engine_profile.resolved_feature("diy").reason
                      if self.engine_profile is not None else
                      "尚未附加游戏进程")
            self._set_status(reason or "自定义写入当前不可用")
            return
        if self.listview_diy.currentRow() < 0:
            return
        n = self.listview_diy.currentRow()
        try:
            addr = int(self.listview_diy.item(n,3).text(),16)
            cnt = int(self.listview_diy.item(n,4).text(),10)
            num = int(self.listview_diy.item(n,1).text(),10)
            if cnt not in (1, 2, 4):
                raise ValueError("自定义字段宽度只能是 1、2 或 4")
            if not 0 <= num < (1 << (cnt * 8)):
                raise ValueError("自定义数值超出 UInt%d 范围" % (cnt * 8))
            if not self.memory_session.validate_range(addr, cnt, write=True):
                raise ValueError("自定义地址不在可写内存页内")
            self.memory_session.write_bytes(addr, num.to_bytes(cnt, "little"))
            self.DIY_Page(1)
        except (ValueError, MemoryAccessError) as exc:
            self._set_status(str(exc))
            return

    def onMk(self):
        if self._detaching or self.memory_session is None or self.engine_profile is None:
            return
        global addr_bisha
        if version == 6 and not self._feature_readable("fatal"):
            self._set_status(self.engine_profile.capability("fatal").reason)
            return
        data = ctypes.c_int()
        a = self.listview_mk.currentRow()
        if a < 0 or (version == 6 and a >= 80) or addr_bisha == 0:
            return
        if version == 6:
            row_address = addr_bisha + a * 16
            row = self.memory_session.read_actual_bytes(row_address, 16)
            pointer_reference = self.engine_profile.addresses.get(
                "fatal_pointer")
            self._fatal_snapshot = RawRecordSnapshot(
                "fatal", row_address, row, None, addr_bisha,
                pointer_reference=pointer_reference,
                pointer_value=(addr_bisha if pointer_reference else None),
                generation="%08X:16:80" % addr_bisha)
            person_controls = [
                self.mk_input_1_1, self.mk_input_1_3, self.mk_input_1_5,
                self.mk_input_1_7, self.mk_input_1_9,
            ]
            level_controls = [
                self.mk_input_1_2, self.mk_input_1_4, self.mk_input_1_6,
                self.mk_input_1_8, self.mk_input_1_10,
            ]
            for index, offset in enumerate((0, 3, 6, 9, 12)):
                person_controls[index].setCurrentIndex(
                    int.from_bytes(row[offset:offset+2], "little"))
                level_controls[index].setText(str(row[offset+2]))
            self.mk_input_1_11.setText(str(row[15]))
            return
        #天赋
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16, ctypes.byref(data), 2, None)
        self.mk_input_1_1.setCurrentIndex(data.value)
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 3, ctypes.byref(data), 2, None)
        self.mk_input_1_3.setCurrentIndex(data.value)
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 6, ctypes.byref(data), 2, None)
        self.mk_input_1_5.setCurrentIndex(data.value)
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 9, ctypes.byref(data), 2, None)
        self.mk_input_1_7.setCurrentIndex(data.value)
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 12, ctypes.byref(data), 2, None)
        self.mk_input_1_9.setCurrentIndex(data.value)
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 2, ctypes.byref(data), 1, None)
        self.mk_input_1_2.setText(str(data.value))
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 5, ctypes.byref(data), 1, None)
        self.mk_input_1_4.setText(str(data.value))
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 8, ctypes.byref(data), 1, None)
        self.mk_input_1_6.setText(str(data.value))
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 11, ctypes.byref(data), 1, None)
        self.mk_input_1_8.setText(str(data.value))
        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 14, ctypes.byref(data), 1, None)
        self.mk_input_1_10.setText(str(data.value))

        self.md.ReadProcessMemory(int(self.p), addr_bisha + a * 16 + 15, ctypes.byref(data), 1, None)
        self.mk_input_1_11.setText(str(data.value))
    
    def saveMk(self):
        global addr_bisha
        if version == 6 and not self._feature_enabled("fatal"):
            self._set_status(self.engine_profile.capability("fatal").reason)
            return
        a = self.listview_mk.currentRow()
        if version == 6:
            if not 0 <= a < 80 or addr_bisha == 0:
                self._set_status("必杀记录或运行时指针无效")
                return
            person_values = [
                self.mk_input_1_1.currentIndex(),
                self.mk_input_1_3.currentIndex(),
                self.mk_input_1_5.currentIndex(),
                self.mk_input_1_7.currentIndex(),
                self.mk_input_1_9.currentIndex(),
            ]
            level_widgets = [
                self.mk_input_1_2, self.mk_input_1_4, self.mk_input_1_6,
                self.mk_input_1_8, self.mk_input_1_10,
            ]
            try:
                levels = [self._parse_uint(widget, 8, "必杀领悟等级")
                          for widget in level_widgets]
                effect_value = self._parse_uint(self.mk_input_1_11, 8, "必杀效果值")
                if any(not 0 <= value <= 0xFFFF for value in person_values):
                    raise ValueError("必杀武将编号超出 UInt16 范围")
                row_address = addr_bisha + a * 16
                pointer_reference = self.engine_profile.addresses.get(
                    "fatal_pointer")
                row = bytearray(16)
                for index, offset in enumerate((0, 3, 6, 9, 12)):
                    row[offset:offset+2] = person_values[index].to_bytes(2, "little")
                    row[offset+2] = levels[index]
                row[15] = effect_value
                replacement = bytes(row)
                self.memory_session.write_actual_bytes(
                    row_address, replacement, verify=False)
                self._fatal_snapshot = RawRecordSnapshot(
                    "fatal", row_address, replacement, None, addr_bisha,
                    pointer_reference,
                    addr_bisha if pointer_reference is not None else None,
                    "%08X:16:80" % addr_bisha)
            except (ValueError, MemoryAccessError) as exc:
                self._set_status(str(exc))
            return
        #天赋
        data = self.mk_input_1_1.currentIndex()
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16, ctypes.byref(ctypes.c_int(data)), 2, None)
        data = self.mk_input_1_3.currentIndex()
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 3, ctypes.byref(ctypes.c_int(data)), 2, None)
        data = self.mk_input_1_5.currentIndex()
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 6, ctypes.byref(ctypes.c_int(data)), 2, None)
        data = self.mk_input_1_7.currentIndex()
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 9, ctypes.byref(ctypes.c_int(data)), 2, None)
        data = self.mk_input_1_9.currentIndex()
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 12, ctypes.byref(ctypes.c_int(data)), 2, None)
        data = int(self.mk_input_1_2.toPlainText())
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 2, ctypes.byref(ctypes.c_int(data)), 1, None)
        data = int(self.mk_input_1_4.toPlainText())
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 5, ctypes.byref(ctypes.c_int(data)), 1, None)
        data = int(self.mk_input_1_6.toPlainText())
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 8, ctypes.byref(ctypes.c_int(data)), 1, None)
        data = int(self.mk_input_1_8.toPlainText())
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 11, ctypes.byref(ctypes.c_int(data)), 1, None)
        data = int(self.mk_input_1_10.toPlainText())
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 14, ctypes.byref(ctypes.c_int(data)), 1, None)

        data = int(self.mk_input_1_11.toPlainText())
        self.md.WriteProcessMemory(int(self.p), addr_bisha + a * 16 + 15, ctypes.byref(ctypes.c_int(data)), 1, None)

    def mousePressEvent(self, event):
        if event.buttons () == QtCore.Qt.LeftButton:
            if event.x()>self.label_pivot.x() and event.x()<self.label_pivot.x()+self.label_pivot.width()\
            and event.y()-20>self.label_pivot.y() and event.y()-20<self.label_pivot.y()+self.label_pivot.height():
                try:
                    x = self._load_cursor_file(self.resource_dir / '准星.cur')
                except OSError as exc:
                    self._set_status(str(exc))
                    return
                win32api.SetCursor(x)
                self.label_pivot.setPixmap(QtGui.QPixmap(""))
                self.mouse_capture = True
    
    def mouseReleaseEvent(self, event):
        if self.mouse_capture == False:
            return
        #还原鼠标的图标
        x = win32gui.LoadImage(0,32512,win32con.IMAGE_CURSOR,0,0,win32con.LR_SHARED)
        win32api.SetCursor(x)
        #还原准星
        pivot_path = self.resource_dir / '准星.png'
        if not pivot_path.exists():
            pivot_path = self.resource_dir / '准星.cur'
        pix = QtGui.QPixmap(str(pivot_path))
        self.label_pivot.setPixmap(pix)
        self.mouse_capture = False
        #捕获进程
        if self.process == NULL:
            self._set_status("未捕获到游戏进程")
            return
        self._detach_process()
        ccz = False
        try:
            module_base, module_size, exe_path = get_process_module(
                self.process.pid)
            process_path = self.process.exe()
            if process_path and not os.path.samefile(process_path, exe_path):
                raise MemoryAccessError("捕获窗口不属于目标进程主模块")
            disk_image = PEImage(exe_path)
            disk_identity = verify_engine_identity(exe_path)
            ccz = True
        except (MemoryAccessError, ProfileError, OSError) as exc:
            ccz = False
            self._set_status(str(exc))
        self.toolBar.setEnabled(ccz)
        self.widget_1.setEnabled(ccz)
        self.widget_2.setEnabled(ccz)
        self.ok = ccz
        if ccz == False:
            self.widget_1.show()
            self.widget_2.hide()
            self.widget_3.hide()
            self.widget_4.hide()
            self.widget_5.hide()
            self.widget_6.hide()
            self.widget_7.hide()
            self.widget_8.hide()
            return

        #确定版本
        global version
        try:
            version_part = int(self.getFileVersion(exe_path))
        except (OSError, KeyError, TypeError, ValueError) as exc:
            if self.version_1.isChecked():
                self._set_status("自动模式无法读取版本资源，请手动选择引擎版本: " +
                                 str(exc))
                self._detach_process()
                self.toolBar.setEnabled(False)
                self.widget_1.setEnabled(False)
                self.widget_2.setEnabled(False)
                return
            version_part = None
        version = self._resolve_engine_version(version_part)

        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        global life
        global cnt_item
        global addr_tianfu
        global addr_zhuanshu
        global addr_bisha
        global condition_change
        try:
            self.memory_session = MemorySession.open_pid(
                self.process.pid, module_base, module_size,
                disk_image.image_base)
            self.memory_session.error_handler = self._set_status
            self.md = self.memory_session
            self.p = self.memory_session.handle
            runtime_image = RuntimeModuleImage(exe_path, self.memory_session)
            runtime_identity = disk_identity
            for attempt in range(20):
                runtime_image.refresh()
                runtime_identity = verify_engine_identity(
                    exe_path, runtime_image)
                if runtime_identity["runtime_anchor_found"]:
                    break
                if attempt < 19:
                    time.sleep(0.1)
            self.engine_profile = detect_composite_profile(
                exe_path, version_part=version_part,
                manual_hint=self._manual_engine_hint(), image=runtime_image)
            self.engine_profile.metadata[
                "identity_diagnostics"] = runtime_identity
            version = int(self.engine_profile.version)
            if version == 2 and self.memory_session.read_u8(0x41A8A0) == 0x55:
                version = 1
                self.engine_profile = legacy_profile(version)
            profile = self.engine_profile
            addr_war = profile.addresses["battle_units"]
            len_war = profile.limits["battle_stride"]
            cnt_wo = profile.limits["battle_ours"]
            cnt_you = profile.limits["battle_friends"]
            cnt_di = profile.limits["battle_enemies"]
            # ``cnt_item`` is the physical metadata-domain count.  Expanded
            # 6.5 has 255 records (0..254); equipment writes use the separate
            # profile equipment domain below and must not truncate this list.
            cnt_item = profile.limits["item_count"]
            addr_tianfu = profile.addresses.get("effect_assign", 0)
            addr_zhuanshu = profile.addresses["exclusive_set"]
            addr_bisha = profile.addresses.get("fatal_table", 0)
            profile.probe_runtime(self.memory_session)
            addr_tianfu = profile.addresses.get("effect_assign", 0)
            addr_bisha = profile.addresses.get("fatal_table", 0)
            # The profile is authoritative.  Keep the historic global only
            # for the no-profile compatibility path used by old callers.
            try:
                life = self.engine_profile.revive_entry_address().to_bytes(
                    4, "little")
            except (AttributeError, ProfileError, ValueError):
                if version == 4:
                    life = b'\x0F\x93\x40\x00'
                elif version == EngineVersion.V66:
                    life = (0x004092C0).to_bytes(4, "little")
                elif version == EngineVersion.V65:
                    life = (0x004092C7).to_bytes(4, "little")
                else:
                    life = (0x004092E0).to_bytes(4, "little")
        except (ProfileError, MemoryAccessError) as exc:
            self._set_status(str(exc))
            self._detach_process()
            self.toolBar.setEnabled(False)
            self.widget_1.setEnabled(False)
            self.widget_2.setEnabled(False)
            return

        effect_feature = self.engine_profile.resolved_feature("effect")
        effect_layout = effect_feature.layout
        self._set_effect_layout(
            isinstance(effect_layout, TableLayoutSpec) and
            effect_layout.codec == "effect-4+2-v1")
        condition_change = version == EngineVersion.V66
        if version in (EngineVersion.V64, EngineVersion.V65):
            data = ctypes.c_int()
            self.md.ReadProcessMemory(int(self.p), 0x4240CA, ctypes.byref(data), 1, None)
            if data.value == 0xFC:
                condition_change = True
        self.war_input_5.setVisible(not condition_change)
        self.war_input_6.setVisible(not condition_change)
        self.war_input_7.setVisible(not condition_change)
        self.war_input_8.setVisible(not condition_change)
        self.war_input_51.setVisible(condition_change)
        self.war_input_52.setVisible(condition_change)
        self.war_input_53.setVisible(condition_change)
        self.war_input_54.setVisible(condition_change)
        self._set_status(self.engine_profile.summary())

        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), 0x401007, ctypes.byref(data), 2, None)
        if version != 6 and data.value == 0xF4E9:
            self.on_action_C_triggered()
            version = -1
        data = ctypes.c_int(0)
        #检查各个按钮的初始状态
        self.md.ReadProcessMemory(int(self.p), 4445556, ctypes.byref(data), 1, None)
        if data.value == 0x74:
            self.checkBox_1.setChecked(False)
        else:
            self.checkBox_1.setChecked(True)
        self.md.ReadProcessMemory(int(self.p), 0x4242B0, ctypes.byref(data), 1, None)
        if data.value == 0x90:
            self.checkBox_2.setChecked(True)
        else:
            self.checkBox_2.setChecked(False)
        self.md.ReadProcessMemory(int(self.p), 0x44E9D3, ctypes.byref(data), 1, None)
        if data.value == 0x74:
            self.checkBox_3.setChecked(False)
        else:
            self.checkBox_3.setChecked(True)
        self.md.ReadProcessMemory(int(self.p), 0x43D5DC, ctypes.byref(data), 1, None)
        if data.value == 0x74:
            self.checkBox_4.setChecked(False)
        else:
            self.checkBox_4.setChecked(True)
        self.md.ReadProcessMemory(int(self.p), 0x43F74F, ctypes.byref(data), 1, None)
        if data.value == 0x55:
            self.checkBox_5.setChecked(False)
        else:
            self.checkBox_5.setChecked(True)
        self.md.ReadProcessMemory(int(self.p), 0x4387B7, ctypes.byref(data), 1, None)
        if data.value == 0x74:
            self.checkBox_6.setChecked(False)
        else:
            self.checkBox_6.setChecked(True)
        self.md.ReadProcessMemory(int(self.p), 0x44E571, ctypes.byref(data), 1, None)
        if data.value == 0x8B:
            self.checkBox_7.setChecked(True)
        else:
            self.checkBox_7.setChecked(False)
        self.md.ReadProcessMemory(int(self.p), addr_war, ctypes.byref(data), 2, None)
        if data.value == 0xFFFF:
            self.checkBox_8.setChecked(False)
        else:
            self.md.ReadProcessMemory(int(self.p), addr_war+0xE, ctypes.byref(data), 1, None)
            if data.value == 7:
                self.checkBox_8.setChecked(False)
            else:
                self.checkBox_8.setChecked(True)
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), 0x404388, ctypes.byref(data), 1, None)
        if data.value == 0x55:
            self.checkBox_9.setChecked(False)
        else:
            self.checkBox_9.setChecked(True)
        self._sync_66_patch_controls()
        self._apply_capabilities()

        #读入data人物列表
        #读取角色列表
        self.listview_data.clear()
        self.war_input_1.clear()
        power_person_inputs = [self.power_input_1_1,
                               self.power_input_1_2,
                               self.power_input_1_3]
        power_job_inputs = [self.power_input_1_4]
        current_effect_layout = self.engine_profile.resolved_feature(
            "effect").layout
        if (isinstance(current_effect_layout, TableLayoutSpec) and
                current_effect_layout.codec == "effect-4+2-v1"):
            power_person_inputs.append(self.power_input_1_4)
            power_job_inputs = [self.power_input_1_9,
                                self.power_input_1_11]
        for control in power_person_inputs + power_job_inputs:
            control.clear()
        self.power_input_2_1.clear()
        self.power_input_2_4.clear()
        self.mk_input_1_1.clear()
        self.mk_input_1_3.clear()
        self.mk_input_1_5.clear()
        self.mk_input_1_7.clear()
        self.mk_input_1_9.clear()
        mem = ctypes.c_uint32()
        name = ctypes.c_byte()
        mem.value = self.memory_session.read_u32(
            self._profile_address("person_pointer", 0x004CEA00))
        person_count = self.engine_profile.limits["person_count"]
        person_stride = self.engine_profile.limits["person_stride"]
        for i in range(0, person_count):
            names = self._read_text(mem.value+i*person_stride+8, 8)
            self.listview_data.addItem(str(i)+":"+names)
            self.war_input_1.addItem(str(i)+":"+names)
            for control in power_person_inputs:
                control.addItem(str(i)+":"+names)
            self.power_input_2_1.addItem(str(i)+":"+names)
            self.power_input_2_4.addItem(str(i)+":"+names)
            self.mk_input_1_1.addItem(str(i)+":"+names)
            self.mk_input_1_3.addItem(str(i)+":"+names)
            self.mk_input_1_5.addItem(str(i)+":"+names)
            self.mk_input_1_7.addItem(str(i)+":"+names)
            self.mk_input_1_9.addItem(str(i)+":"+names)
        for control in power_person_inputs:
            control.addItem("空")
        self.power_input_2_1.addItem("空")
        self.power_input_2_4.addItem("空")
        self.mk_input_1_1.addItem("空")
        self.mk_input_1_3.addItem("空")
        self.mk_input_1_5.addItem("空")
        self.mk_input_1_7.addItem("空")
        self.mk_input_1_9.addItem("空")

        #读取兵种名称
        self.data_input_31.clear()
        job_names = self._profile_address("job_names", 0x005000D0)
        for i in range(0,80):
            names=b""
            for j in range(0,9):
                self.md.ReadProcessMemory(int(self.p), job_names+i*0x9+j, ctypes.byref(name), 1, None)
                names+=name
            try:
                names=names.decode("gbk")
            except:
                names="非法字符"
            self.data_input_31.addItem(str(i)+":"+names)
            for control in power_job_inputs:
                control.addItem(str(i)+":"+names)
        for control in power_job_inputs:
            control.addItem("空")
        #读取宝物名称
        self.data_input_32.clear()
        self.data_input_35.clear()
        self.data_input_38.clear()
        self.item_input_1.clear()
        self.power_input_2_2.clear()
        self.power_input_2_5.clear()
        self.power_input_3_1.clear()
        self.power_input_3_2.clear()
        self.power_input_3_3.clear()
        self.power_input_3_5.clear()
        self.power_input_3_6.clear()
        self.power_input_3_7.clear()
        item_name_size = self.engine_profile.limits.get("item_name_size", 12)
        item_runtime_table = self.engine_profile.addresses.get("item_table", 0)
        item_table_66 = None
        if version in (EngineVersion.V65, EngineVersion.V66):
            try:
                item_table_66 = self.engine_profile.item_table_66()
            except ProfileError:
                pass
        for i in range(0,cnt_item):
            '''if i >=87 and i <= 129:
                continue'''
            if item_table_66 is not None:
                names = self.engine_profile.item_row(i).name
            elif item_runtime_table > 0:
                names = self._read_text(
                    item_runtime_table + i * 25,
                    item_name_size)
            else:
                names = "物品%d" % i
            self.data_input_32.addItem(str(i)+":"+names)
            self.data_input_35.addItem(str(i)+":"+names)
            self.data_input_38.addItem(str(i)+":"+names)
            self.item_input_1.addItem(str(i)+":"+names)
            self.power_input_2_2.addItem(str(i)+":"+names)
            self.power_input_2_5.addItem(str(i)+":"+names)
            self.power_input_3_1.addItem(str(i)+":"+names)
            self.power_input_3_2.addItem(str(i)+":"+names)
            self.power_input_3_3.addItem(str(i)+":"+names)
            self.power_input_3_5.addItem(str(i)+":"+names)
            self.power_input_3_6.addItem(str(i)+":"+names)
            self.power_input_3_7.addItem(str(i)+":"+names)
        self.data_input_32.addItem("空")
        self.data_input_35.addItem("空")
        self.data_input_38.addItem("空")
        self.item_input_1.addItem("空")
        self.power_input_2_2.addItem("空")
        self.power_input_2_5.addItem("空")
        self.power_input_3_1.addItem("空")
        self.power_input_3_2.addItem("空")
        self.power_input_3_3.addItem("空")
        self.power_input_3_5.addItem("空")
        self.power_input_3_6.addItem("空")
        self.power_input_3_7.addItem("空")

        self.listview_item.clear()
        for i in range(0,200):
            self.listview_item.addItem("空")

        #读取消耗品名称
        self.listview_item_2.clear()
        consumable_mapping = self._consumable_mapping()
        if consumable_mapping is not None:
            self.listview_item_2.setEnabled(True)
            self.item_input_4.setEnabled(
                self._feature_writable("item_consumables"))
            for item_id in self._consumable_ids():
                if version == EngineVersion.V66:
                    if item_table_66 is None:
                        item_name = "名称不可用"
                    else:
                        item_name = self.engine_profile.item_row(item_id).name
                elif item_table_66 is not None:
                    item_name = self.engine_profile.item_row(item_id).name
                elif item_runtime_table > 0:
                    item_name = self._read_text(
                        item_runtime_table + item_id * 25, item_name_size)
                else:
                    item_name = "物品%d" % item_id
                self.listview_item_2.addItem("%d:%s" % (item_id, item_name))
        elif version in (EngineVersion.V64, EngineVersion.V65,
                          EngineVersion.V66):
            resolved = self.engine_profile.resolved_feature("item_consumables")
            self.listview_item_2.addItem("消耗品映射未验证")
            self.listview_item_2.setToolTip(
                resolved.reason or
                self.engine_profile.capability("item_consumables").reason)
            self.listview_item_2.setEnabled(False)
            self.item_input_4.setEnabled(False)
        elif version in (EngineVersion.V63_MP, EngineVersion.V63,
                         EngineVersion.V62, EngineVersion.V61):
            self.listview_item_2.setEnabled(True)
            self.item_input_4.setEnabled(True)
            for i in range(0,43):
                names=b""
                for j in range(0,12):
                    self.md.ReadProcessMemory(int(self.p), 0x4A19BF+i*25+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gb18030")
                except:
                    print(names)
                    names="非法字符"
                self.listview_item_2.addItem(names)
        #读取天赋/必杀
        self.listview_item_3.clear()
        self.listview_mk.clear()
        if version in (EngineVersion.V65, EngineVersion.V66):
            try:
                self._populate_profile_effect_names()
            except ProfileError as exc:
                self._set_status(
                    "读取%s特效名称失败: %s" %
                    (self.engine_profile.display_version, exc))
                self.item_save_2.setEnabled(False)
            if version == EngineVersion.V66:
                data_file = Path(exe_path).with_name("Data.e5")
                try:
                    with data_file.open("rb") as stream:
                        stream.seek(59540)
                        fatal_rows = stream.read(80 * 16)
                    if len(fatal_rows) != 80 * 16:
                        raise OSError("必杀名称表长度不足")
                    for i in range(0,80):
                        raw = fatal_rows[i*16:i*16+11].split(b"\0", 1)[0]
                        try:
                            names = raw.decode("gbk")
                        except UnicodeDecodeError:
                            names = "非法字符"
                        self.listview_mk.addItem(str(i)+":"+names)
                except OSError as exc:
                    self._set_status("读取6.6必杀名称失败: " + str(exc))
                    self.item_save_4.setEnabled(False)
            else:
                for i in range(0,80):
                    names=b""
                    for j in range(0,11):
                        self.md.ReadProcessMemory(int(self.p), 0x4A7510+i*16+j, ctypes.byref(name), 1, None)
                        names+=name
                    try:
                        names=names.decode("gbk")
                    except:
                        print(names)
                        names="非法字符"
                    self.listview_mk.addItem(str(i)+":"+names)
        elif version == EngineVersion.V64:
            for i in range(0,180):
                names=b""
                for j in range(0,15):
                    self.md.ReadProcessMemory(int(self.p), 0x4FF560+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_item_3.addItem(str(i)+":"+names)
            for i in range(0,80):
                names=b""
                for j in range(0,11):
                    self.md.ReadProcessMemory(int(self.p), 0x4A7510+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_mk.addItem(str(i)+":"+names)
        elif version in (-1, EngineVersion.V63_MP, EngineVersion.V63):
            for i in range(0,144):
                names=b""
                for j in range(0,15):
                    self.md.ReadProcessMemory(int(self.p), 0x4FF7A0+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_item_3.addItem(str(i)+":"+names)
            for i in range(0,36):
                names=b""
                for j in range(0,11):
                    self.md.ReadProcessMemory(int(self.p), 0x4FF551+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_mk.addItem(str(i)+":"+names)
        elif version == 3:
            self.md.ReadProcessMemory(int(self.p), 0x4F0000+i*16+j, ctypes.byref(name), 1, None)
            if name.value != 0:
                addr = 0x4F0000
                cnt = 126
            else:
                addr = 0x4FF960
                cnt = 91
            for i in range(0,cnt):
                names=b""
                for j in range(0,15):
                    self.md.ReadProcessMemory(int(self.p), addr+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_item_3.addItem(str(i)+":"+names)
            for i in range(0,36):
                names=b""
                for j in range(0,11):
                    self.md.ReadProcessMemory(int(self.p), 0x4FF710+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_mk.addItem(str(i)+":"+names)
        else:
            for i in range(0,88):
                names=b""
                for j in range(0,15):
                    self.md.ReadProcessMemory(int(self.p), 0x4FFA80+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_item_3.addItem(str(i)+":"+names)
            for i in range(0,36):
                names=b""
                for j in range(0,11):
                    self.md.ReadProcessMemory(int(self.p), 0x4FF710+i*16+j, ctypes.byref(name), 1, None)
                    names+=name
                try:
                    names=names.decode("gbk")
                except:
                    print(names)
                    names="非法字符"
                self.listview_mk.addItem(str(i)+":"+names)

        #初始设置
        self.listview_data.setCurrentRow(0)
        self.war_kind_1.setChecked(True)
        lock_list.clear()
        lock_hp.clear()
        lock_mp.clear()
        lock_ids.clear()
        self._auto_revive_attempts.clear()
        self.listview_war_2.clear()
        self.listview_item.setCurrentRow(0)
        self.listview_item_2.setCurrentRow(0)
        self.listview_item_3.setCurrentRow(0)
        self.listview_mk.setCurrentRow(0)

        self.onData()
        self.onWar(0)
        self.onItem()
        self.onVar()
        self.DIY_Page(0)
        self.diy_input_4.setCurrentIndex(0)

        #锁血、锁蓝和自动复活在 UI 线程定时执行。
        self.lock_timer.start()

    def mouseMoveEvent(self, event):
        if self.mouse_capture == True:
            point = win32api.GetCursorPos()
            hwnd = win32gui.WindowFromPoint(point)
            title = win32gui.GetWindowText(hwnd)
            clsname = win32gui.GetClassName(hwnd)
            hread_id, self.process_id = win32process.GetWindowThreadProcessId(hwnd)  #线程ID  进程ID
            self.process = psutil.Process(self.process_id)
            self.label_window1.setText(title)
            self.label_window2.setText(clsname)

    def changeEvent(self, event):
        if event.type() == QtCore.QEvent.ActivationChange:
            if self.window().isActiveWindow():
                if self.ok == True:
                    self.onData()
                    self.onWar(0)
                    self.onItem()
                    self.onPower()
                    self.onVar()
                    self.DIY_Page(1)

    def recal(self):
        if len(self.listview_data.selectedIndexes())<=0:
            return
        n = self.listview_data.selectedIndexes()[0].row()
        mem = ctypes.c_uint32()
        mem.value = self.memory_session.read_u32(
            self._profile_address("person_pointer", 0x004CEA00))
        try:
            hk = my_hook(self.memory_session, self.engine_profile)
            hk.recal(self.memory_session, mem.value+0x48*n)
        except HookError as exc:
            self._set_status(str(exc))

    def refreshFangzhen(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        if self.war_input_11.currentIndex() == 3 or self.war_input_11.currentIndex() == 5:
            self.war_12.show()
            self.war_input_12.show()
            self.war_input_12.clear()
            data2 = ctypes.c_int()
            data = ctypes.c_int()
            name = ctypes.c_byte()
            mem = ctypes.c_uint32()
            mem.value = self.memory_session.read_u32(
                self._profile_address("person_pointer", 0x004CEA00))
            for i in range(0,cnt_wo + cnt_you + cnt_di):
                self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war, ctypes.byref(data), 2, None)
                if data.value!=65535:
                    self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war + 5, ctypes.byref(data2), 1, None)
                    names = self._read_text(
                        mem.value+data.value*0x48+8, 8)
                    self.md.ReadProcessMemory(int(self.p), addr_war+i*len_war+4, ctypes.byref(data2), 1, None)
                    self.war_input_12.addItem(str(data2.value)+":"+names, i)
                self.war_input_12.setCurrentIndex(0)
        else:
            self.war_12.hide()
            self.war_input_12.hide()

    def onConnect(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        data = ctypes.c_int()
        war_code = self._selected_war_slot()
        if war_code < 0:
            return
        self.md.ReadProcessMemory(int(self.p), addr_war + len_war*war_code, ctypes.byref(data), 2, None)
        self.listview_data.setCurrentRow(data.value)
        self.on_action_M_triggered()

    def onKill(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        x1 = int(self.war_input_47.toPlainText())
        y1 = int(self.war_input_48.toPlainText())
        x2 = int(self.war_input_49.toPlainText())
        y2 = int(self.war_input_50.toPlainText())
        for i in range(cnt_wo + cnt_you,cnt_wo + cnt_you + cnt_di):
            x0 = ctypes.c_int(0)
            y0 = ctypes.c_int(0)
            self.md.ReadProcessMemory(int(self.p), addr_war+len_war*i+6, ctypes.byref(x0), 1, None)
            self.md.ReadProcessMemory(int(self.p), addr_war+len_war*i+7, ctypes.byref(y0), 1, None)
            if x1<=x0.value and x2>=x0.value and y1<=y0.value and y2>=y0.value:
                self.md.WriteProcessMemory(int(self.p), addr_war+len_war*i+0x10, ctypes.byref(ctypes.c_int(0)), 4, None)

    def onLife(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        global life
        war_code = self._selected_war_slot()
        if war_code < 0:
            self._set_status("请先选择战场单位")
            return
        if self.memory_session is None or self.engine_profile is None:
            self._set_status("尚未附加可写游戏进程")
            return
        total_slots = cnt_wo + cnt_you + cnt_di
        if not 0 <= war_code < total_slots:
            self._set_status("战场槽位越界: %d/%d" % (war_code, total_slots))
            return
        unit = addr_war + len_war * war_code
        try:
            if not self.memory_session.validate_range(unit, len_war,
                                                      write=False):
                raise MemoryAccessError("战场槽位地址不可读: 0x%08X" % unit)
            data_id = self.memory_session.read_u16(unit)
            hp_before = self.memory_session.read_u32(unit + 0x10)
            view_before = self.memory_session.read_u8(unit + 0x0C)
            if data_id == 0xFFFF:
                raise ValueError("当前槽位为空，不能复活")
            if hp_before != 0 or view_before != 3:
                raise ValueError("目标不是死亡状态(HP=%d, 显示状态=%d)" %
                                 (hp_before, view_before))
            if self.war_input_16.currentIndex() == 0:
                direction = self.memory_session.read_u8(unit + 0x0F)
            else:
                direction = self.war_input_16.currentIndex() - 1
            x = int(self.war_input_9.toPlainText().strip())
            y = int(self.war_input_10.toPlainText().strip())
            if not 0 <= direction <= 3:
                raise ValueError("复活方向必须在 0..3 范围内")
            if not 0 <= x <= 0xFF or not 0 <= y <= 0xFF:
                raise ValueError("复活坐标必须在 0..255 范围内")
            entry = self.engine_profile.revive_entry_address()
            before = (data_id, hp_before, view_before,
                      self.memory_session.read_u8(unit + 0x06),
                      self.memory_session.read_u8(unit + 0x07))
        except (MemoryAccessError, ProfileError, ValueError, TypeError) as exc:
            self._set_status("复活前检查失败: 槽位=%d，%s" % (war_code, exc))
            return
        try:
            hk = my_hook(self.memory_session, self.engine_profile)
            hk.life(self.memory_session, direction, x, y, data_id, life)
            data_after = self.memory_session.read_u16(unit)
            hp_after = self.memory_session.read_u32(unit + 0x10)
            view_after = self.memory_session.read_u8(unit + 0x0C)
            if data_after != data_id:
                raise HookError(
                    "复活后 Data ID 变化: 0x%04X -> 0x%04X" %
                    (data_id, data_after))
            if hp_after == 0 and view_after == 3:
                raise HookError("远程调用完成但槽位仍为死亡状态")
            self.war_life.setEnabled(False)
            self.war_life.setStyleSheet("background-color:rgb(255, 255, 255);\ncolor:rgb(190,190,190)")
            self.onWar(1)
            self._set_status(
                "复活成功：版本=%s 入口=0x%08X 槽位=%d Data ID=%d 参数=(%d,%d,%d)" %
                (self.engine_profile.display_version, entry, war_code,
                 data_id, direction, x, y))
        except (HookError, MemoryAccessError, ProfileError, ValueError) as exc:
            self._set_status(
                "复活失败：版本=%s 入口=0x%08X 槽位=%d Data ID=%d 参数=(%d,%d,%d)：%s" %
                (self.engine_profile.display_version, entry, war_code,
                 before[0], direction, x, y, exc))

    def onLock(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        global lock_ids
        if self.listview_war.currentRow() == -1:
            return
        war_code = self._selected_war_slot()
        if war_code < 0:
            return
        if war_code in lock_list:
            return
        self.listview_war_2.addItem(self.listview_war.item(self.listview_war.currentRow()).text())
        lock_list.append(war_code)
        data = ctypes.c_int()
        self.md.ReadProcessMemory(int(self.p), addr_war+war_code*len_war,
                                  ctypes.byref(data), 2, None)
        lock_ids.append(data.value)
        #HPCur
        lock_hp.append(self.memory_session.read_u32(
            addr_war+war_code*len_war+0x10))
        #MPCur
        lock_mp.append(self.memory_session.read_u32(
            addr_war+war_code*len_war+0x14))

    def offLock(self):
        if self.listview_war_2.currentRow() == -1:
            return
        del lock_list[self.listview_war_2.currentRow()]
        del lock_hp[self.listview_war_2.currentRow()]
        del lock_mp[self.listview_war_2.currentRow()]
        del lock_ids[self.listview_war_2.currentRow()]
        self.listview_war_2.takeItem(self.listview_war_2.currentRow())

    def LockThread(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di

        global lock_list
        global lock_hp
        global lock_mp
        global lock_ids
        global auto_life

        if not self.ok or self.memory_session is None:
            return
        if not psutil.pid_exists(self.process_id):
            self._detach_process()
            self.toolBar.setEnabled(False)
            self.widget_1.setEnabled(False)
            self.widget_2.setEnabled(False)
            self.widget_1.show()
            for widget in (self.widget_2, self.widget_3, self.widget_4,
                           self.widget_5, self.widget_6, self.widget_7,
                           self.widget_8):
                widget.hide()
            self._set_status("游戏进程已退出")
            return

        if auto_life:
            try:
                now = time.monotonic()
                attempts = getattr(self, "_auto_revive_attempts", {})
                self._auto_revive_attempts = attempts
                for key, submitted_at in tuple(attempts.items()):
                    if now - submitted_at > 2.0:
                        attempts.pop(key, None)
                helper = my_hook(self.memory_session, self.engine_profile)
                entry = self.engine_profile.revive_entry_address()
                for i in range(0,cnt_wo):
                    unit = addr_war+i*len_war
                    code = self.memory_session.read_u16(unit)
                    hp = self.memory_session.read_u32(unit+0x10)
                    view = self.memory_session.read_u8(unit+0x0C)
                    for old_key in tuple(attempts):
                        if old_key[0] == i and old_key[1] != code:
                            attempts.pop(old_key, None)
                    if hp != 0 or code == 0xFFFF or view != 3:
                        attempts.pop((i, code), None)
                        continue
                    if hp == 0 and code != 0xFFFF and view == 3:
                        dedup_key = (i, code)
                        if dedup_key in attempts:
                            continue
                        direction = self.memory_session.read_u8(unit+0x0F)
                        x = self.memory_session.read_u8(unit+0x06)
                        y = self.memory_session.read_u8(unit+0x07)
                        attempts[dedup_key] = now
                        helper.life(
                            self.memory_session, direction, x, y, code, life)
                        code_after = self.memory_session.read_u16(unit)
                        hp_after = self.memory_session.read_u32(unit+0x10)
                        view_after = self.memory_session.read_u8(unit+0x0C)
                        if code_after != code:
                            raise HookError(
                                "自动复活复读 Data ID 变化: 槽位=%d, "
                                "0x%04X->0x%04X" %
                                (i, code, code_after))
                        if hp_after == 0 and view_after == 3:
                            raise HookError(
                                "自动复活远程调用完成但目标仍为死亡状态: "
                                "槽位=%d, Data ID=%d" % (i, code))
                        attempts.pop(dedup_key, None)
                        self._set_status(
                            "自动复活成功：版本=%s 入口=0x%08X "
                            "槽位=%d Data ID=%d 参数=(%d,%d,%d)" %
                            (self.engine_profile.display_version, entry, i,
                             code, direction, x, y))
            except (MemoryAccessError, HookError, ProfileError, ValueError) as exc:
                auto_life = False
                self.checkBox_9.setChecked(False)
                self._set_status("自动复活已停止：%s" % exc)

        if not (len(lock_list) == len(lock_hp) == len(lock_mp) == len(lock_ids)):
            lock_list.clear()
            lock_hp.clear()
            lock_mp.clear()
            lock_ids.clear()
            self.listview_war_2.clear()
            return
        for index in range(len(lock_list)-1, -1, -1):
            slot = lock_list[index]
            unit = addr_war+slot*len_war
            try:
                if self.memory_session.read_u16(unit) != lock_ids[index]:
                    del lock_list[index]
                    del lock_hp[index]
                    del lock_mp[index]
                    del lock_ids[index]
                    self.listview_war_2.takeItem(index)
                    continue
                self.memory_session.write_u32(unit+0x10, lock_hp[index])
                self.memory_session.write_u32(unit+0x14, lock_mp[index])
            except MemoryAccessError as exc:
                self._set_status(str(exc))
                return

    def getFileVersion(self, exe_path=None):
        import os
        info = win32api.GetFileVersionInfo(
            exe_path or self.process.exe(), os.sep)
        ms = info['FileVersionMS']
        ls = info['FileVersionLS']
        version = '%d' % (win32api.LOWORD(ls))
        print(version)
        return version
    def onTurn(self):
        global addr_war
        global len_war
        global cnt_wo
        global cnt_you
        global cnt_di
        if self.war_input_16.currentIndex() <=0:
            return
        data = ctypes.c_int()
        war_code = self._selected_war_slot()
        if war_code < 0:
            return
        self.md.ReadProcessMemory(int(self.p), addr_war + len_war*war_code, ctypes.byref(data), 2, None)

        if version == 6 and not self._feature_enabled("turn_refresh_direction"):
            direction = self.war_input_16.currentIndex()-1
            self.memory_session.write_u8(addr_war + len_war*war_code + 0x0F,
                                         direction)
            self._set_status(self.engine_profile.capability(
                "turn_refresh_direction").reason + "；已仅保存方向字段")
            self.onWar(1)
            return
        try:
            hk = my_hook(self.memory_session, self.engine_profile)
            hk.changeDir(self.memory_session,
                         self.war_input_16.currentIndex()-1, data.value)
            self.onWar(1)
        except HookError as exc:
            self._set_status(str(exc))

    def allItem(self, n):
        global cnt_item
        warehouse = self._profile_address("warehouse", 0x004B0783)
        item_table_address = self._profile_address("item_table", 0x004A1140)
        if (n in (2, 3) and version in
                (EngineVersion.V64, EngineVersion.V65, EngineVersion.V66)):
            if not self._feature_writable("item_consumables"):
                self._set_status(self.engine_profile.resolved_feature(
                    "item_consumables").reason or "消耗品计数当前不可写")
                return
            if n == 3 and not self._feature_writable("warehouse"):
                self._set_status("清空需要仓库与消耗品同时可写")
                return
            entries = []
            if n == 3:
                entries.extend((warehouse + slot * 3, b"\xFF")
                               for slot in range(0, 200))
            value = 255 if n == 2 else 0
            entries.extend(self._consumable_entry(item_id, value)
                           for item_id in self._consumable_ids())
            try:
                self.memory_session.write_transaction(entries)
                self.onItem()
            except (MemoryAccessError, ValueError) as exc:
                self._set_status(str(exc))
            return
        if version in (EngineVersion.V65, EngineVersion.V66):
            try:
                level = min(self._parse_uint(self.item_input_2, 8, "宝物等级"), 9)
                experience = self._parse_uint(self.item_input_3, 8, "宝物经验")
            except (ValueError, TypeError) as exc:
                self._set_status(str(exc))
                return
            try:
                item_table = self.engine_profile.item_table_66()
            except ProfileError as exc:
                self._set_status(str(exc))
                return
            treasures = treasure_ids_66(
                item_table, self.engine_profile.limits["equipment_count"],
                str(self.engine_profile.metadata.get(
                    "item_row_codec", "item-66-15-v1")))
            empty_slots = [slot for slot in range(0, 200)
                           if self.memory_session.read_u8(
                               warehouse + slot * 3) == 255]
            for slot, item_id in zip(empty_slots, treasures):
                address = warehouse + slot * 3
                self.memory_session.write_u8(address, item_id)
                self.memory_session.write_u8(address + 1, level)
                item_experience = 250 if experience == 255 and level != 9 else experience
                self.memory_session.write_u8(address + 2, item_experience)
            if len(treasures) > len(empty_slots):
                self._set_status("仓库已满，剩余 %d 件宝物未写入" %
                                 (len(treasures) - len(empty_slots)))
            self.onItem()
            return
        if n == 1:
            a = 0
            i = 0
            while True:
                if i >= 200:
                    self._set_status("仓库已满，批量写入已停止")
                    break
                data = ctypes.c_int()
                self.md.ReadProcessMemory(int(self.p), warehouse + i * 3, ctypes.byref(data), 1, None)
                if data.value != 255:
                    i += 1
                    continue
                if version in (EngineVersion.V63_MP, EngineVersion.V63,
                                EngineVersion.V62, EngineVersion.V61):
                    if a == 87:
                        a = 130
                    if a == cnt_item:
                        break
                else:
                    if a == cnt_item:
                        break

                data = ctypes.c_int()
                self.md.ReadProcessMemory(int(self.p), item_table_address+a*25+0x14, ctypes.byref(data), 1, None)
                if data.value == 255:
                    a += 1
                    continue
                data = ctypes.c_int(a)
                self.md.WriteProcessMemory(int(self.p), warehouse + i * 3, ctypes.byref(data), 1, None)
                data = ctypes.c_int(int(self.item_input_2.toPlainText()))
                if data.value > 9:
                    data.value = 9
                self.md.WriteProcessMemory(int(self.p), warehouse + i * 3 + 1, ctypes.byref(data), 1, None)
                data2 = ctypes.c_int(int(self.item_input_3.toPlainText()))
                if data2.value == 255 and data.value != 9:
                    data2.value = 250
                self.md.WriteProcessMemory(int(self.p), warehouse + i * 3 + 2, ctypes.byref(data2), 1, None)
                a += 1
        if n == 2:
            if version >= 1:
                for i in range(0,43):
                    data = ctypes.c_int(255)
                    if i <= 16:
                        self.md.WriteProcessMemory(int(self.p), 0x4B09DB + i, ctypes.byref(data), 1, None)
                    else:
                        self.md.WriteProcessMemory(int(self.p), 0x510c80 + i-17, ctypes.byref(data), 1, None)
            else:
                for i in range(0,105):
                    data = ctypes.c_int(255)
                    self.md.WriteProcessMemory(int(self.p), 0x510c80 + i, ctypes.byref(data), 1, None)
        if n == 3:
            for i in range(0,200):
                data = ctypes.c_int(255)
                self.md.WriteProcessMemory(int(self.p), warehouse + i * 3, ctypes.byref(data), 1, None)
            if version >= 1:
                for i in range(0,43):
                    data = ctypes.c_int(0)
                    if i <= 16:
                        self.md.WriteProcessMemory(int(self.p), 0x4B09DB + i, ctypes.byref(data), 1, None)
                    else:
                        self.md.WriteProcessMemory(int(self.p), 0x510c80 + i-17, ctypes.byref(data), 1, None)
            else:
                for i in range(0,105):
                    data = ctypes.c_int(0)
                    self.md.WriteProcessMemory(int(self.p), 0x510c80 + i, ctypes.byref(data), 1, None)
        self.onItem()

    def DIY_Page(self, n):
        if self._detaching or self.memory_session is None or self.engine_profile is None:
            return
        global cnt_item
        while self.listview_diy.rowCount() != 0:
            self.listview_diy.removeRow(0)
        dirname = self.base_dir / "DIY.csv"
        try:
            with open(dirname, encoding="utf-8-sig", newline="") as csvfile:
                a = 1
        except OSError:
            return
        #初始化
        if n == 0:
            self.diy_input_4.clear()
            with open(dirname, encoding="utf-8-sig", newline="") as csvfile:
                csv_reader = csv.reader(csvfile)
                for row in csv_reader:
                    if len(row) < 6:
                        continue
                    if row[0] != '' and row[1] == '' and row[2] == '' and row[3] == '' and row[4] == '' and row[5] == '':
                        self.diy_input_4.addItem(row[0])
            return
        #显示数据
        with open(dirname, encoding="utf-8-sig", newline="") as csvfile:
            csv_reader = csv.reader(csvfile)
            table = -1
            a = 0
            for row in csv_reader:
                if len(row) < 6:
                    self._set_status("DIY.csv 存在不足六列的记录")
                    continue
                if table != self.diy_input_4.currentIndex():
                    if row[0] != '' and row[1] == '' and row[2] == '' and row[3] == '' and row[4] == '' and row[5] == '':
                        table += 1
                        if table == self.diy_input_4.currentIndex():
                            next(csv_reader)
                    continue
                if row[0] != '' and row[1] == '' and row[2] == '' and row[3] == '' and row[4] == '' and row[5] == '':
                    break
                try:
                    xunhuan = int(row[4])
                    width = int(row[3], 10)
                    base_address = int(row[2], 16)
                except ValueError:
                    self._set_status("DIY.csv 存在无效地址、宽度或循环次数")
                    continue
                if width not in (1, 2, 4):
                    self._set_status("DIY.csv 中的字段宽度只能是 1、2 或 4")
                    continue
                for i in range(0,xunhuan):
                    '''if (int(row[2],16) >= 0x492FC8 and int(row[2],16) < 0x496FC8) or\
                        (int(row[2],16) >= 0x502000 and int(row[2],16) < 0x50A000):
                        continue'''
                    address = base_address+i*width
                    if not self.memory_session.validate_range(address, width, write=False):
                        self._set_status("DIY.csv 地址 0x%X 不可读" % address)
                        continue
                    self.listview_diy.insertRow(a)
                    if xunhuan == 1:
                        self.listview_diy.setItem(a,0,QtWidgets.QTableWidgetItem(row[0]))
                    else:
                        self.listview_diy.setItem(a,0,QtWidgets.QTableWidgetItem(row[0]+"_"+str(i)))
                    self.listview_diy.setItem(a,3,QtWidgets.QTableWidgetItem('{:X}'.format(address,16)))
                    self.listview_diy.setItem(a,4,QtWidgets.QTableWidgetItem(str(width)))
                    self.listview_diy.setItem(a,5,QtWidgets.QTableWidgetItem(row[5]))
                    data = ctypes.c_int()
                    self.md.ReadProcessMemory(int(self.p), address, ctypes.byref(data), width, None)
                    self.listview_diy.setItem(a,1,QtWidgets.QTableWidgetItem(str(data.value)))
                    if row[1] == "战场编号":
                        tmp = data.value
                        if not 0 <= tmp < cnt_wo + cnt_you + cnt_di:
                            strs = "无效编号"
                        else:
                            self.md.ReadProcessMemory(int(self.p), addr_war+tmp*len_war, ctypes.byref(data), 2, None)
                            if data.value >= 1024 or self.listview_data.item(data.value) is None:
                                strs = "空"
                            else:
                                strs = self.listview_data.item(data.value).text()
                        self.listview_diy.setItem(a,2,QtWidgets.QTableWidgetItem(strs))
                    elif row[1] == "Data编号":
                        if data.value >= 1024:
                            strs = "无效编号"
                        else:
                            strs = self.listview_data.item(data.value).text()
                        self.listview_diy.setItem(a,2,QtWidgets.QTableWidgetItem(strs))
                    elif row[1] == "宝物":
                        if data.value >= cnt_item:
                            strs = "无效编号"
                        else:
                            strs = self.item_input_1.itemText(data.value)
                        self.listview_diy.setItem(a,2,QtWidgets.QTableWidgetItem(strs))
                    a += 1
