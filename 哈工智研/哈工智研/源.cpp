#include <cstdio>

int main() {
    int a = 123;
    double pi = 3.1415926;

    // 宽度与对齐
    printf("默认: |%d|\n", a);        // |123|
    printf("右对齐: |%5d|\n", a);      // |  123|
    printf("补零: |%05d|\n", a);      // |00123|
    printf("左对齐: |%-5d|\n", a);     // |123  |

    // 小数精度
    printf("保留2位: %12f\n", pi);    // 3.14
    printf("保留4位: %.4f\n", pi);    // 3.1416 (四舍五入)

    return 0;
}