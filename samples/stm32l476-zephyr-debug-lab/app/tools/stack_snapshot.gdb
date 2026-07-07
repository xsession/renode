# GDB helper commands for lab exercises

define btfull
  bt full
  info registers
end

define mark
  call debug_force_trace_marker()
end

define whereirq
  info registers ipsr
  bt
end

# Example use:
# source tools/stack_snapshot.gdb
# break debug_halt_here
# mark
