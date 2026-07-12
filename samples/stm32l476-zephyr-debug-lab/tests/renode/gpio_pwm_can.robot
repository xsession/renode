*** Settings ***
Documentation     Skeleton for a Renode Robot test. Adapt paths and expected strings to your installed Renode version.

*** Variables ***
${SCRIPT}         app/renode/stm32l476_debug.resc

*** Test Cases ***
Boot And Produce Trace
    [Documentation]    Start Renode and verify that firmware produces boot trace.
    Log    This is a template. Use Renode's Robot integration in your environment.
    Log    Expected trace contains: STM32L476 Zephyr Debug Lab
    Log    Expected periodic events: GPIO, PWM, CAN
