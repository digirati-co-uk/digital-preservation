class Result:

    def __init__(self, success:bool, error:str=None):
        self.success = success
        self.failure = not success
        self.value = None
        self.error = error

    @staticmethod
    def success(value):
        result = Result(True)
        result.value = value
        return result