/// <reference types="vite/client" />

// 不声明 `*.vue` 模块：vue-tsc 直接解析 SFC，声明通配模块会把所有组件的 props 与
// defineEmits 抹成 Record<string, never>，模板传错属性、事件名拼错都查不出来