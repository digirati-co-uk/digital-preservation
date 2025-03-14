from util import get_parent

if __name__ == '__main__':
    print(get_parent("aa/bb/cc"))
    print(get_parent("/aa/bb/cc"))
    print(get_parent("/aa/bb/cc/"))
    print(get_parent("/aa/bb/cc/d"))