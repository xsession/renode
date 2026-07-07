cmake_minimum_required(VERSION 3.20.0)
find_package(Zephyr REQUIRED HINTS $ENV{ZEPHYR_BASE})
project(stm32l476_zephyr_debug_lab)

target_sources(app PRIVATE
  src/main.c
  src/app_trace.c
  src/gpio_demo.c
  src/pwm_demo.c
  src/can_demo.c
  src/fault_injection.c
)
